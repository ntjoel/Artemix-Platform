---
title: 02 Avvio e stop produzione
description: Sequenza sicura per avviare, arrestare e diagnosticare una mancata partenza.
image: Images/02_camera_monitor.png
image_caption: Vista telecamere usata per verificare il primo pezzo dopo l'avvio.
---

## Sequenza operativa

![Vista telecamere e stato processo](Images/02_camera_monitor.png)

| ID | Fase | Cosa controllare | Azione operatore | Quando chiamare supporto |
|---|---|---|---|---|
| 1 | Pre-avvio | Ricetta, utente, allarmi e protezioni macchina. | Non premere Start finche il prodotto non coincide con la ricetta. | Se esiste un blocco senza descrizione. |
| 2 | Start | Il comando e abilitato e la macchina e pronta. | Premere una volta e attendere `RUNNING`. | Se resta `STOPPED` o compare un hold. |
| 3 | Primo prodotto | Immagini aggiornate, un incremento Total, esito plausibile. | Osservare almeno i primi pezzi del lotto. | Se immagine, contatore o scarto sono fuori sequenza. |
| 4 | Produzione | Velocita e contatori avanzano senza popup ripetitivi. | Monitorare Quality Index e banner. | Se una camera smette di aggiornarsi. |
| 5 | Stop | Il ciclo deve terminare in modo ordinato. | Premere Stop e attendere `STOPPED`. | Se le uscite restano attive dopo lo stop. |
| 6 | Close HMI | Comando riservato ai ruoli tecnici. | Chiudere solo a macchina ferma. | Se lo shutdown non termina o riporta errori. |

Da versione `3.1.2.0`, i contatori e i difetti hanno priorita' rispetto al rendering delle camere.
La HMI ignora un aggiornamento immagine vecchio se nel frattempo e' gia' arrivato un risultato piu'
recente dello stesso ruolo. Le immagini archiviate in produzione sono catturate dal record del
pezzo: `_A.bmp` e' sempre raw. Per NOK e non classificati, `_Z.jpg` contiene gli overlay VisionPro
quando il rendering e' disponibile; in caso di timeout o errore ActiveX viene salvato il raw e il
log riporta `SAVE_IMAGE_ANNOTATED_FAILED` o `SAVE_IMAGE_ANNOTATED_TIMEOUT`. Il rendering usa una
coda limitata e non ritarda il risultato del pezzo.

Dalla release `3.1.5.4`, la cattura annotata verifica che il display mostri
ancora il record dello stesso prodotto. Se il record e' gia' stato sostituito,
il sistema salva il raw e registra `ANNOTATED_CACHE_SKIPPED` invece di associare
al pezzo un'immagine del ciclo successivo.

## Mancata partenza

![Monitor eventi per diagnosi della mancata partenza](Images/03_events_monitor.png)

| ID | Indicazione | Significato | Azione |
|---|---|---|---|
| A | Runtime hold | Una pagina tecnica o un servizio mantiene VisionPro fermo. | Uscire dall'editor; non forzare Start ripetutamente. |
| B | Allarme bloccante | La logica allarmi impedisce la marcia. | Rimuovere la causa e fare Acknowledge. |
| C | OPC UA required | Solo se configurato come vincolante, manca il server. | Controllare stato OPC UA o chiamare manutenzione. |
| D | VisionPro stopped | Il job non e in RunContinuous. | Verificare VPP, camere e ultimi eventi. |
| E | Board connection failed | Le schede I/O richieste non sono disponibili. | Verificare alimentazione, driver e uso da altri tool. |
