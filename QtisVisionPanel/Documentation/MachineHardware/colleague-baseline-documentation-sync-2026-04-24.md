# Colleague Baseline Documentation Sync - 2026-04-24

## Scopo

Questo documento registra il fatto che la repo del collega in:

- `S:\Sorgenti e software\VISIONQAI\QtisVisionPanel`

e' stata aggiornata oggi con la stessa struttura documentale e di istruzioni che nel locale gia' guidava il progetto.

L'obiettivo era evitare una divergenza tra:

- repo del collega
- repo locale completa
- futuro punto di integrazione del sottosistema I/O/encoder

## Cosa e' stato promosso sulla baseline del collega

Sono stati portati nella repo del collega:

- `AGENTS.md`
- `README.md`
- `TASK_REQUEST_TEMPLATE.md`
- tutta `Documentation\MachineHardware`
- il pacchetto commissioning completo `Docs\Commissioning\01..07`

## Documento di riferimento sul repo del collega

Per leggere cosa e' stato aggiunto e perche', usare:

- `S:\Sorgenti e software\VISIONQAI\QtisVisionPanel\Documentation\MachineHardware\colleague-baseline-documentation-promotion-2026-04-24.md`

Per leggere invece il piano di integrazione I/O sulla baseline del collega:

- `S:\Sorgenti e software\VISIONQAI\QtisVisionPanel\Documentation\MachineHardware\colleague-baseline-io-integration-plan-2026-04-24.md`

## Stato condiviso emerso

Dal confronto tra repo del collega, repo locale e standalone I/O e' emerso che:

- il repo del collega ha gia' un layer I/O/encoder dentro il progetto completo
- il repo locale contiene una cornice documentale e storica molto piu' ricca
- lo standalone contiene il sottosistema I/O/encoder piu' maturo, ma non ancora integrato nel progetto completo del collega

## Blocco tecnico ancora aperto

La baseline del collega oggi non e' ancora build-clean.

Quindi il prossimo lavoro corretto non e':

- copiare a mano il tool evoluto dentro il progetto completo

ma:

1. ripristinare la build baseline del collega
2. fondere in modo controllato il sottosistema I/O/encoder
3. riallineare poi il locale completo a quella baseline aggiornata

## Perche' questo documento serve anche nel locale

Serve a ricordare che da oggi:

- la repo del collega e' di nuovo leggibile come baseline comune
- il locale non deve divergere silenziosamente
- ogni prossima integrazione importante dovra' lasciare traccia sia sul repo del collega sia sul locale
