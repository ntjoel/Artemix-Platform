# Preparazione installer e aggiornamento - 2026-09-18

## Baseline e perimetro

Rigenerazione della release esistente `3.1.5.4-r18` dai sorgenti del workspace
`D:\Pulsar\Developper\NewPanel\NewPanel\QtisVisionPanel`, comprese le modifiche
locali gia' presenti. Nessuna nuova modifica al comportamento applicativo:
la versione assembly resta `3.1.5.4`. Non sono stati eseguiti installer sul PC
di sviluppo o comandi di movimento macchina.

## Analisi documentale per il rilascio

Inventariati i 104 documenti Markdown sotto Docs e Documentation; approfonditi
gli indici, la baseline, l'archivio release, le procedure installer e le note
recenti rilevanti per la distribuzione. Questa verifica documentale non
sostituisce il collaudo funzionale dell'intera applicazione.

| Area | Contratto da preservare / contenuto distribuito |
|---|---|
| Macchina e I/O | Config.xml autorevole; mapping, quote ed encoder macchina distinti dalla ricetta |
| Ricette e camere | Profilo opzionale inspectionViewConfiguration, fallback legacy, trigger e matrice per vista |
| VisionPro e AI | Ticket prodotto e protezione risultati obsoleti; soglia Classify per vista; ONNX HMI isolato in AiRuntime |
| Immagini | Raw _A.bmp e annotata _Z.jpg correlate; timeout con fallback raw |
| Localizzazione | Cataloghi software aggiornati, personalizzazioni Language runtime preservate nell'updater |
| Database | MinimumScore aggiunta idempotente da 3.1.5.3; verificare permessi e inizializzazione al primo avvio |
| Diagnostica / operatore | Manuali, commissioning, DataInspector, sensori PC e note tecniche inclusi secondo il progetto |
| Installazione | Full con seed e prerequisiti; UpdateOnly con solo ApplicationBin, backup differenziale e journal rollback |

Riallineati il riferimento al media corrente nella guida architetturale e la
procedura operativa dell'updater. Le note storiche restano identificate come
storiche. Il percorso sorgente predefinito dello script OneDrive non esiste su
questo PC: la sincronizzazione deve specificare il workspace corrente tramite
`-SourcePath`, mantenendo la destinazione condivisa canonica e senza `-Mirror`.

## Build e artefatti

Build con MSBuild di Visual Studio 2022 Professional, eseguibile
`MSBuild\Current\Bin\amd64\MSBuild.exe`, configurazione Release, piattaforma
x64. Esito: 0 errori, 68 warning (API obsolete e metodi async senza await).
Il primo tentativo con MSBuild non amd64 ha fallito nell'host GenerateResource;
il tentativo x64 ha compilato HMI e worker ONNX correttamente.

Comando builder:

```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\Build-QtisInstallerMedia.ps1
```

Output previsti:

- `D:\QtisInstallerOutput\QtisVisionPanel_3.1.5.4-r18`
- `D:\QtisInstallerOutput\QtisVisionPanel_Update_3.1.5.4-r18`

Le evidenze autorevoli della generazione sono `build-summary.json`,
`payload-integrity.sha256`, `installer-validation.json` e il report aggiuntivo
`release-verification.json` nelle cartelle media. Un nome cartella o una nota
storica non costituiscono prova di validazione.

## Collaudo e limiti

La validazione dei pacchetti verifica hash, contenuti applicativi, cataloghi,
manuali e separazione delle dipendenze. Non prova il ciclo macchina reale.
Prima dell'uso produttivo eseguire caricamento/cambio ricetta, start/stop,
trigger hardware, campioni GOOD/NOK/sotto soglia, correlazione immagini,
contatori, DB, allarmi e recovery secondo le guide di commissioning.

Per un PC privo di DAQNavi e senza rete resta necessario il pacchetto driver
offline del sito: XNavi non garantisce da solo tutti i driver. Il seed completo
deriva da C:\QtisVision ed e' specifico della macchina sorgente.
