# Colleague Baseline Build Sync - 2026-04-24

## Scope

Questo documento registra il riallineamento minimo del repo locale utente rispetto alle correzioni introdotte nella baseline collega del 2026-04-24.

Repo locale:

- `C:\Users\chouikha\QtisVisionPanel`

## Allineamento applicato

### Post-build VisionPro

File aggiornato:

- `QtisVisionPanel.csproj`

Il `PostBuildEvent` e' stato riallineato alla baseline collega in modo che:

- provi a creare `VisionProDependencies` come giunzione locale
- non renda la build fallita se il filesystem/permessi non consentono la creazione del link
- espliciti che la giunzione VisionPro va completata su checkout locale NTFS della macchina

## Relazione con la baseline collega

Questo sync locale segue il documento:

- `S:\Sorgenti e software\VISIONQAI\QtisVisionPanel\Documentation\MachineHardware\colleague-baseline-build-restoration-2026-04-24.md`

## Stato

Questo step non e' ancora il riallineamento completo al repo collega aggiornato.

Serve come hardening minimo per evitare divergenze sul comportamento di build mentre si prosegue con:

- fusione I/O/encoder
- configurazione macchina
- pagine tool complete nel progetto full
