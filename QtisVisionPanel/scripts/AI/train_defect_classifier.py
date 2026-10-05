# -*- coding: utf-8 -*-
"""
QtisVisionPanel - Training offline del classificatore difetti (Fase 3b).

Lanciato dall'HMI come PROCESSO ESTERNO (pulsante "Addestra modello" in PC Diagnostics
-> Controlli AI). Legge il dataset raccolto da TrainingDataCollectionService (cartelle
pezzo con sidecar label.json + immagine TOP), addestra una CNN leggera DA ZERO (nessun
peso preaddestrato: funziona anche su macchine senza internet) ed esporta il modello in
formato ONNX compatibile col contratto di OnnxDefectClassifier:

  - input  NCHW float32 [1, C, H, W]  (C=1 se grayscale, altrimenti 3 RGB)
  - preprocessing: resize (W,H) + (pixel - mean) / std   (stessi mean/std della config HMI)
  - output logits [1, 2]: classe 0 = OK, classe 1 = NOK

Protocollo verso l'HMI (stdout, una riga per messaggio):
  PROGRESS|epoch=3/20|train_loss=0.412|val_acc=0.92|val_nok_recall=0.88
  RESULT|{"ok": true, "model_path": "...", ...}

Exit code: 0 = ok, 2 = dataset insufficiente, 3 = dipendenze Python mancanti, 1 = errore.

Dipendenze: pip install torch pillow numpy onnx
"""

import argparse
from datetime import datetime
import json
import math
import os
import random
import sys
import warnings


def emit(line):
    print(line, flush=True)


def fail(exit_code, message, extra=None):
    payload = {"ok": False, "error": message}
    if extra:
        payload.update(extra)
    emit("RESULT|" + json.dumps(payload))
    sys.exit(exit_code)


# Sigle immagine per camera nella cartella pezzo (stessa convenzione di
# OnnxDefectClassifier.GetImageTags). Una camera ha due sigle quando puo' comparire in due viste
# con lo stesso record: Side (_F_) / Left (_L_), Right (_RI_) / Rear (_R_).
CAMERA_IMAGE_TAGS = {
    "top": ("_T_",),
    "side": ("_F_", "_L_"),
    "right": ("_RI_",),
    "rear": ("_R_",),
    "front": ("_FR_",),
    "bottom": ("_B_",),
}


def parse_args():
    p = argparse.ArgumentParser(description="Train QtisVision defect classifier (ONNX export)")
    p.add_argument("--self-test", action="store_true", help="Verifica Python e dipendenze senza training")
    p.add_argument("--camera", default="top", choices=sorted(CAMERA_IMAGE_TAGS.keys()),
                   help="Camera da addestrare (chiave labels.<camera> di label.json): "
                        "top (_T_), side (_F_/_L_), right (_RI_), rear (_R_), front (_FR_), bottom (_B_)")
    p.add_argument("--dataset-root", help="Radice cartelle immagini (contiene le cartelle pezzo con label.json)")
    p.add_argument("--output", help="Percorso file .onnx di destinazione")
    p.add_argument("--input-width", type=int, default=224)
    p.add_argument("--input-height", type=int, default=224)
    p.add_argument("--grayscale", action="store_true")
    p.add_argument("--norm-mean", type=float, default=0.0)
    p.add_argument("--norm-std", type=float, default=255.0)
    p.add_argument("--epochs", type=int, default=20)
    p.add_argument("--min-per-class", type=int, default=10)
    p.add_argument("--val-split", type=float, default=0.2)
    p.add_argument("--recommended-val-per-class", type=int, default=10,
                   help="Campioni validation consigliati per classe; sotto questa soglia il modello e' marcato provvisorio")
    p.add_argument("--seed", type=int, default=42)
    return p.parse_args()


def import_training_dependencies():
    try:
        import numpy as np  # noqa: F401
        from PIL import Image
        import torch
        import torch.nn as nn
        import onnx
    except ImportError as ex:
        fail(3, "Dipendenze Python mancanti ({}). Installare con: pip install torch pillow numpy onnx".format(ex))

    versions = {
        "python": sys.version.split()[0],
        "torch": getattr(torch, "__version__", "unknown"),
        "pillow": getattr(Image, "__version__", "unknown"),
        "numpy": getattr(np, "__version__", "unknown"),
        "onnx": getattr(onnx, "__version__", "unknown"),
    }
    return np, Image, torch, nn, onnx, versions


def find_camera_image(piece_folder, suffixes):
    """Immagine della camera nel pezzo (stessa convenzione di OnnxDefectClassifier.GetImageTags).

    suffixes: sigle della camera da CAMERA_IMAGE_TAGS (es. ("_F_", "_L_") per side, perche' un
    job Side mostrato nella vista Left viene salvato con la sigla _L_). Preferita la raw *A.bmp;
    fallback all'annotata *Z.jpg.
    """
    try:
        names = os.listdir(piece_folder)
    except OSError:
        return None
    for ending in ("A.bmp", "Z.jpg"):
        for suffix in suffixes:
            found = sorted(n for n in names if n.endswith(suffix + ending))
            if found:
                return os.path.join(piece_folder, found[0])
    return None


def resolve_sample_timestamp(sidecar, sidecar_path):
    """Timestamp numerico stabile per un holdout cronologico.

    I sidecar HMI correnti usano ISO locale senza timezone. Per dataset legacy o valori non
    validi si usa la data del file, mantenendo comunque un ordinamento deterministico.
    """
    raw = sidecar.get("timestamp")
    if raw:
        try:
            return datetime.fromisoformat(str(raw).replace("Z", "+00:00")).timestamp()
        except (TypeError, ValueError, OverflowError):
            pass
    try:
        return os.path.getmtime(sidecar_path)
    except OSError:
        return 0.0


def scan_dataset(root, camera, suffix):
    """Cerca ricorsivamente i sidecar label.json e associa l'immagine della camera indicata.

    Etichetta PER-CAMERA: TOP usa labels.top e mantiene il fallback alla label globale per i
    dataset storici. SIDE richiede labels.side: usare la label globale contaminerebbe il modello
    Side quando il NOK del pezzo e' stato causato soltanto dalla camera Top.

    NB encoding: si legge con 'utf-8-sig', che gestisce correttamente sia i sidecar CON BOM
    (l'HMI li scrive con BOM UTF-8) sia quelli senza. Con il semplice 'utf-8' il BOM iniziale
    farebbe fallire json.load e i file verrebbero scartati in silenzio.
    """
    samples = []  # dict: image, label (0=OK/1=NOK), timestamp, piece_folder
    skipped_no_image = 0
    skipped_bad_json = 0
    skipped_bad_label = 0
    skipped_missing_camera_label = 0
    for dirpath, _dirnames, filenames in os.walk(root):
        if "label.json" not in filenames:
            continue
        sidecar_path = os.path.join(dirpath, "label.json")
        try:
            with open(sidecar_path, "r", encoding="utf-8-sig") as f:
                sidecar = json.load(f)
        except (OSError, ValueError):
            skipped_bad_json += 1
            continue
        labels_block = sidecar.get("labels")
        has_labels_block = isinstance(labels_block, dict) and len(labels_block) > 0
        per_camera = labels_block if isinstance(labels_block, dict) else {}
        raw_label = per_camera.get(camera)
        # Fallback alla label GLOBALE solo per i sidecar LEGACY (senza blocco 'labels'), per non
        # perdere i dataset storici del TOP. Se il blocco 'labels' esiste ma non contiene questa
        # camera, la camera NON e' stata ispezionata su quel pezzo: si salta (non si etichetta la
        # camera col difetto di un'altra camera).
        if raw_label is None and camera == "top" and not has_labels_block:
            raw_label = sidecar.get("label", "")
        if raw_label is None:
            skipped_missing_camera_label += 1
            continue
        label = str(raw_label).strip().upper()
        if label not in ("OK", "NOK"):
            skipped_bad_label += 1
            continue
        image = find_camera_image(dirpath, suffix)
        if image is None:
            skipped_no_image += 1
            continue
        samples.append({
            "image": image,
            "label": 0 if label == "OK" else 1,
            "timestamp": resolve_sample_timestamp(sidecar, sidecar_path),
            "piece_folder": dirpath,
        })
    return samples, skipped_no_image, skipped_bad_json, skipped_bad_label, skipped_missing_camera_label


def main():
    args = parse_args()

    emit("PROGRESS|stage=python_import|message=Caricamento dipendenze AI (torch/pillow/numpy/onnx)")
    np, Image, torch, nn, onnx, versions = import_training_dependencies()
    emit("PROGRESS|stage=python_import|message=Dipendenze AI caricate")

    if args.self_test:
        emit("PROGRESS|stage=self-test|message=Python e dipendenze AI disponibili")
        emit("RESULT|" + json.dumps({"ok": True, "mode": "self-test", "versions": versions}))
        sys.exit(0)

    if not args.dataset_root or not args.output:
        fail(1, "--dataset-root e --output sono obbligatori quando non si usa --self-test")

    random.seed(args.seed)
    torch.manual_seed(args.seed)

    suffix = CAMERA_IMAGE_TAGS.get(args.camera, ("_T_",))

    emit("PROGRESS|stage=scan|camera={}|message=Scansione dataset in corso".format(args.camera))
    samples, skipped_no_image, skipped_bad_json, skipped_bad_label, skipped_missing_camera_label = scan_dataset(
        args.dataset_root, args.camera, suffix)
    ok_count = sum(1 for sample in samples if sample["label"] == 0)
    nok_count = sum(1 for sample in samples if sample["label"] == 1)
    # I contatori di scarto sono ESPLICITI: se il dataset risulta vuoto si vede subito il perche'
    # (json illeggibile, label non OK/NOK, immagine della camera mancante) invece di uno 0 muto.
    emit("PROGRESS|stage=scan|camera={}|ok={}|nok={}|senza_etichetta_camera={}|senza_immagine={}|json_illeggibile={}|label_ignota={}".format(
        args.camera, ok_count, nok_count, skipped_missing_camera_label, skipped_no_image,
        skipped_bad_json, skipped_bad_label))

    if ok_count < args.min_per_class or nok_count < args.min_per_class:
        fail(2, "Dataset insufficiente: servono almeno {} campioni per classe (trovati OK={}, NOK={}).".format(
            args.min_per_class, ok_count, nok_count),
            {"ok_count": ok_count, "nok_count": nok_count})

    channels = 1 if args.grayscale else 3
    width, height = args.input_width, args.input_height

    def load_image(path):
        import numpy as np
        with Image.open(path) as img:
            img = img.convert("L" if args.grayscale else "RGB").resize((width, height), Image.BILINEAR)
            arr = np.asarray(img, dtype="float32")
        arr = (arr - args.norm_mean) / args.norm_std
        if args.grayscale:
            arr = arr[None, :, :]                    # HW -> CHW
        else:
            arr = arr.transpose(2, 0, 1)             # HWC -> CHW
        return torch.from_numpy(arr.copy())

    # Holdout cronologico stratificato: per ogni classe, i campioni piu' recenti restano
    # esclusivamente in validation. E' piu' conservativo del vecchio split casuale, che poteva
    # distribuire immagini quasi identiche/consecutive sia nel train sia nella validation.
    by_class = {0: [s for s in samples if s["label"] == 0],
                1: [s for s in samples if s["label"] == 1]}
    train_set, val_set = [], []
    for label, items in by_class.items():
        items.sort(key=lambda sample: (sample["timestamp"], sample["image"].lower()))
        n_val = max(1, int(math.ceil(len(items) * args.val_split)))
        n_val = min(n_val, len(items) - 1)
        val_set.extend(items[-n_val:])
        train_set.extend(items[:-n_val])
    random.shuffle(train_set)
    val_ok_count = sum(1 for sample in val_set if sample["label"] == 0)
    val_nok_count = sum(1 for sample in val_set if sample["label"] == 1)
    validation_qualified = (val_ok_count >= args.recommended_val_per_class and
                            val_nok_count >= args.recommended_val_per_class)
    validation_status = "qualified" if validation_qualified else "provisional"
    emit("PROGRESS|stage=split|strategy=chronological_stratified_holdout|train={}|validation={}|val_ok={}|val_nok={}|status={}|message=Dataset diviso in train/validation".format(
        len(train_set), len(val_set), val_ok_count, val_nok_count, validation_status))
    if not validation_qualified:
        emit("PROGRESS|stage=validation_warning|status=provisional|message=Validazione provvisoria: consigliati almeno {} OK e {} NOK nel solo holdout; disponibili OK={} NOK={}".format(
            args.recommended_val_per_class, args.recommended_val_per_class,
            val_ok_count, val_nok_count))

    # CNN leggera da zero (nessun download pesi): adatta a dataset piccoli e PC industriali CPU-only.
    class SmallCnn(nn.Module):
        def __init__(self):
            super(SmallCnn, self).__init__()
            self.features = nn.Sequential(
                nn.Conv2d(channels, 16, 3, padding=1), nn.BatchNorm2d(16), nn.ReLU(), nn.MaxPool2d(2),
                nn.Conv2d(16, 32, 3, padding=1), nn.BatchNorm2d(32), nn.ReLU(), nn.MaxPool2d(2),
                nn.Conv2d(32, 64, 3, padding=1), nn.BatchNorm2d(64), nn.ReLU(), nn.MaxPool2d(2),
                nn.Conv2d(64, 64, 3, padding=1), nn.BatchNorm2d(64), nn.ReLU(),
                nn.AdaptiveAvgPool2d(1),
            )
            self.classifier = nn.Sequential(nn.Flatten(), nn.Dropout(0.3), nn.Linear(64, 2))

        def forward(self, x):
            return self.classifier(self.features(x))

    device = torch.device("cpu")
    model = SmallCnn().to(device)
    emit("PROGRESS|stage=model|message=Modello CNN inizializzato su CPU")

    # Pesi di classe contro lo sbilanciamento (di solito molti OK, pochi NOK).
    total = float(ok_count + nok_count)
    class_weights = torch.tensor([total / (2.0 * ok_count), total / (2.0 * nok_count)], dtype=torch.float32)
    criterion = nn.CrossEntropyLoss(weight=class_weights)
    optimizer = torch.optim.Adam(model.parameters(), lr=1e-3)

    batch_size = 16

    def iterate(dataset, train_mode):
        indices = list(range(len(dataset)))
        if train_mode:
            random.shuffle(indices)
        for start in range(0, len(indices), batch_size):
            batch = [dataset[i] for i in indices[start:start + batch_size]]
            xs = torch.stack([load_image(sample["image"]) for sample in batch]).to(device)
            ys = torch.tensor([sample["label"] for sample in batch], dtype=torch.long).to(device)
            yield xs, ys

    def evaluate():
        model.eval()
        correct = 0
        nok_total = 0
        nok_hit = 0
        confusion = [[0, 0], [0, 0]]  # [reale][predetto]
        with torch.no_grad():
            for xs, ys in iterate(val_set, False):
                pred = model(xs).argmax(dim=1)
                correct += int((pred == ys).sum())
                for y_true, y_pred in zip(ys.tolist(), pred.tolist()):
                    confusion[y_true][y_pred] += 1
                    if y_true == 1:
                        nok_total += 1
                        if y_pred == 1:
                            nok_hit += 1
        acc = correct / float(len(val_set)) if val_set else 0.0
        nok_recall = (nok_hit / float(nok_total)) if nok_total else 0.0
        return acc, nok_recall, confusion

    best_acc = -1.0
    best_state = None
    best_metrics = None
    total_train_batches = max(1, (len(train_set) + batch_size - 1) // batch_size)
    for epoch in range(1, args.epochs + 1):
        model.train()
        epoch_loss = 0.0
        batches = 0
        emit("PROGRESS|stage=train|epoch={}/{}|message=Inizio epoca".format(epoch, args.epochs))
        for batch_index, (xs, ys) in enumerate(iterate(train_set, True), start=1):
            optimizer.zero_grad()
            loss = criterion(model(xs), ys)
            loss.backward()
            optimizer.step()
            loss_value = loss.detach().item()
            epoch_loss += loss_value
            batches += 1
            if batch_index == 1 or batch_index == total_train_batches or batch_index % 5 == 0:
                emit("PROGRESS|stage=train|epoch={}/{}|batch={}/{}|loss={:.4f}".format(
                    epoch, args.epochs, batch_index, total_train_batches, loss_value))
        emit("PROGRESS|stage=validation|epoch={}/{}|message=Validazione epoca".format(epoch, args.epochs))
        val_acc, nok_recall, confusion = evaluate()
        if val_acc > best_acc:
            best_acc = val_acc
            best_state = {k: v.clone() for k, v in model.state_dict().items()}
            best_metrics = {"val_accuracy": val_acc, "val_nok_recall": nok_recall, "confusion": confusion, "epoch": epoch}
        emit("PROGRESS|epoch={}/{}|train_loss={:.4f}|val_acc={:.3f}|val_nok_recall={:.3f}".format(
            epoch, args.epochs, epoch_loss / max(1, batches), val_acc, nok_recall))

    if best_state is not None:
        model.load_state_dict(best_state)
    model.eval()

    out_dir = os.path.dirname(os.path.abspath(args.output))
    if out_dir and not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    output_abs = os.path.abspath(args.output)
    tmp_output = output_abs + ".tmp"
    tmp_metrics = output_abs + ".metrics.json.tmp"
    for stale in (tmp_output, tmp_metrics):
        try:
            if os.path.exists(stale):
                os.remove(stale)
        except OSError:
            pass

    emit("PROGRESS|stage=export|message=Export ONNX su file temporaneo")
    dummy = torch.zeros(1, channels, height, width, dtype=torch.float32)
    # Mantiene l'exporter TorchScript gia' collaudato anche con PyTorch 2.8. Il relativo avviso
    # annuncia solo un cambio di default futuro e non richiede un warning operativo in HMI.
    with warnings.catch_warnings():
        warnings.filterwarnings(
            "ignore",
            message="You are using the legacy TorchScript-based ONNX export.*",
            category=DeprecationWarning)
        torch.onnx.export(
            model, dummy, tmp_output,
            input_names=["input"], output_names=["logits"],
            opset_version=12, do_constant_folding=True)
    onnx_model = onnx.load(tmp_output)
    onnx.checker.check_model(onnx_model)

    metrics = {
        "ok": True,
        "model_path": output_abs,
        "camera": args.camera,
        "classes": {"0": "OK", "1": "NOK"},
        "input": {"width": width, "height": height, "channels": channels,
                  "norm_mean": args.norm_mean, "norm_std": args.norm_std, "layout": "NCHW"},
        "dataset": {"ok": ok_count, "nok": nok_count, "train": len(train_set),
                    "val": len(val_set), "skipped_no_image": skipped_no_image,
                    "skipped_bad_json": skipped_bad_json, "skipped_bad_label": skipped_bad_label,
                    "skipped_missing_camera_label": skipped_missing_camera_label},
        "validation": {
            "strategy": "chronological_stratified_holdout",
            "status": validation_status,
            "qualified": validation_qualified,
            "recommended_per_class": args.recommended_val_per_class,
            "ok": val_ok_count,
            "nok": val_nok_count,
            "note": ("Holdout cronologico sufficiente per entrambe le classi."
                     if validation_qualified else
                     "Metriche provvisorie: raccogliere piu' campioni recenti, soprattutto NOK, prima dell'abilitazione operativa."),
        },
        "metrics": best_metrics or {},
        "epochs": args.epochs,
    }

    os.replace(tmp_output, output_abs)

    try:
        with open(tmp_metrics, "w", encoding="utf-8") as f:
            json.dump(metrics, f, indent=2)
        os.replace(tmp_metrics, output_abs + ".metrics.json")
    except OSError:
        pass  # il sidecar metriche e' best-effort: il RESULT sotto resta la fonte per l'HMI

    emit("RESULT|" + json.dumps(metrics))
    sys.exit(0)


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as ex:  # qualunque errore: riportato all'HMI in formato RESULT
        fail(1, "{}: {}".format(type(ex).__name__, ex))
