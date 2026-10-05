---
title: Indice documentazione operatore
description: Struttura canonica del manuale visualizzato nella HMI e criterio di manutenzione.
image: Images/Main_window.png
image_caption: Schermata principale usata come riferimento nel manuale operatore.
---

# Manuale operatore Qtis Vision Panel

La HMI mostra soltanto i file numerati `NN_*.md`. Questo evita che note tecniche, piani di lavoro o
documenti di sviluppo compaiano nel manuale usato in linea.

## Documento principale

| Documento | Destinatario | Contenuto |
|---|---|---|
| `00_Documentazione_Tecnica_Globale.md` | Operatore, capolinea, manutentore | Manuale illustrato completo: ogni pagina ha immagine, ID e tabella delle funzioni. |

## Procedure rapide

| Documento | Destinatario | Quando usarlo |
|---|---|---|
| `01_Panoramica_Pannello.md` | Operatore | Primo accesso, lettura stato macchina e navigazione. |
| `02_Avvio_Stop_Produzione.md` | Operatore | Avvio, arresto e mancata partenza. |
| `03_Ricette_Allarmi_Assistenza.md` | Operatore / capolinea | Cambio formato, allarmi, raccolta dati per supporto. |
| `04_Controllo_3D_Top3D.md` | Operatore / Expert | Lettura immagini e misure Top3D/Top2D. |
| `05_PowerFlex525_Inverter_Nastro.md` | Expert / Installer | Diagnostica e parametrizzazione inverter. |
| `06_Data_Analysis_Dashboard.md` | Operatore / qualita | Filtri, grafici ed esportazione PDF. |
| `07_Classificazione_AI_Camere.md` | Operatore / Expert | Lettura di classe, score e badge AI nelle viste camera. |
| `08_MultiShot_Right_Bottom.md` | Expert / Installer | Commissioning MultiShot Left, Right e Bottom. |

## Regole editoriali

1. Usare una schermata reale, senza dati sensibili.
2. Inserire una tabella con colonna `ID`, funzione, significato, azione e condizione di supporto.
3. Separare chiaramente azioni operatore da attività Expert/Installer.
4. Non documentare path fissi quando il valore arriva da `Config.xml` o dalla configurazione macchina.
5. Aggiornare il manuale quando cambiano UI, permessi, ricette, allarmi o flussi macchina.

## Verifica prima della release

| Controllo | Esito richiesto |
|---|---|
| Immagini referenziate | Tutti i file esistono in `Docs/Manual/Images`. |
| Tabelle | Ogni procedura operativa usa ID stabili e azioni leggibili. |
| HMI | Il primo documento aperto e il manuale `00`; README e note tecniche non sono elencati. |
| PDF | Titoli, immagini e tabelle non risultano vuoti nell'esportazione. |
| Ruoli | I comandi tecnici sono indicati come Expert/Installer/Administrator. |
