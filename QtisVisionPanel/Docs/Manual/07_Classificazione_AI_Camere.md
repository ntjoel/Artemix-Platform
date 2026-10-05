---
title: 07 Classificazione AI nelle viste camera
description: Guida operatore per leggere classe, score e stato del classificatore VisionPro nelle viste camera.
image: Images/02_camera_monitor.png
image_caption: Area Overview in cui la card di classificazione compare sotto le feature della camera.
---

## Card classificazione AI

![Area telecamere e controlli di produzione](Images/02_camera_monitor.png)

La card compare sotto le feature della camera quando il job VisionPro pubblica
un risultato di classificazione e l'ispezione ricetta `Classificazione AI` e'
abilitata. Puo' essere presente nelle viste `Top`, `Top3D`, `Side`, `Left`,
`Front`, `Rear`, `Right` e `Bottom`.

| ID | Campo | Cosa mostra | Azione operatore | Quando chiamare supporto |
|---|---|---|---|---|
| 1 | Icona AI | Identifica un controllo di classificazione immagine. | Verificare che la card appartenga alla camera attesa. | Se appare nella vista sbagliata. |
| 2 | Classe | Nome restituito dal modello VisionPro, per esempio `OK`, `NOK` o una classe prodotto. | Confrontare il testo con il difetto visibile. | Se la classe resta vuota o non cambia tra prodotti diversi. |
| 3 | Score | Confidenza del classificatore; viene mostrata come percentuale quando il valore e' compreso tra 0 e 1 oppure tra 1 e 100. | Usarlo come informazione diagnostica, non come comando macchina. | Se lo score e' impossibile, non numerico o sempre assente. |
| 4 | Badge GOOD | La classe dello stesso ciclo e' una delle etichette conformi: `OK`, `GOOD`, `PASS`, `PASSED` o `COMPLIANT`. | Nessuna azione se classe, immagine e altre feature sono coerenti. | Se la card e' verde mentre il prodotto e' chiaramente difettoso. |
| 5 | Badge NO GOOD | La classe non e' conforme. Vale anche per nomi difetto liberi, per esempio `Sealing Open`. | Controllare prodotto, contatore `Classificazione AI` e abilitazione scarto ricetta. | Se il pezzo non viene gestito secondo la configurazione ispezioni. |
| 6 | Badge NON CLASSIFICATO | La classe era accettata ma lo score e' inferiore alla soglia della vista. La HMI mostra classe `Unclassify` e conserva lo score misurato. | Verificare prodotto, illuminazione, messa a fuoco e copertura del training. | Se il caso e' noto ma resta sotto soglia. |
| 7 | Contatore AI | Riga autonoma nei contatori difetto; non appartiene a saldatura o controllo superficie. | Usarlo per seguire i NOK del classificatore. | Se aumenta due volte per lo stesso pezzo. |

## Abilitazione nella ricetta

![Pagina ricetta con controlli di ispezione e scarto.](Images/recipe_manager1.png)

| ID | Campo | Cosa significa | Azione autorizzata | Risultato |
|---|---|---|---|---|
| 1 | Classificazione AI - ispezione | Abilita la decisione GOOD/NOK del Classify VisionPro. | Expert/Installer: abilitarla solo se il job pubblica la classe prevista. | La card e il contatore diventano operativi. |
| 2 | Classificazione AI - scarto | Autorizza lo scarto fisico per un NOK AI. | Expert/Installer: abilitarla dopo il collaudo dei campioni. | Con OFF il NOK e' registrato ma non scarta per questa famiglia. |
| 3 | Salva ricetta | Persiste i due flag nel file ricetta. | Salvare e ricaricare la ricetta, poi verificare un pezzo GOOD e uno NOK. | L'impostazione resta attiva ai riavvii. |
| 4 | Ricetta storica | Le vecchie ricette non contengono i nuovi flag. | Nessuna azione automatica; la funzione parte disabilitata. | Il comportamento precedente non cambia. |

## Configurazione per vista camera

La pagina `Inspection Configuration` lavora sulla ricetta attualmente caricata.
Per ogni vista presente nel VPP consente di scegliere l'uso della camera, le
ispezioni realmente pubblicate dal relativo ToolBlock e le classi Classify
considerate conformi.

| ID | Campo | Cosa significa | Azione autorizzata | Risultato |
|---|---|---|---|---|
| 1 | Uso camera - Machine | Eredita l'abilitazione del punto trigger globale. | Usarlo come scelta normale quando la ricetta impiega la camera. | Restano validi board, canale, polarita' e quota macchina. |
| 2 | Uso camera - Enabled | Forza attivo il trigger per questa ricetta. | Expert/Installer: usarlo solo se il punto camera esiste e il VPP contiene il job. | La vista partecipa ad acquisizione, attesa risultati e display. |
| 3 | Uso camera - Disabled | Esclude una camera secondaria dalla ricetta. | Expert/Installer: usarlo per prodotti che non richiedono quella vista; TOP/TOP3D non puo' essere disabilitata. | Nessun trigger richiesto e nessun timeout per risultato mancante. |
| 4 | Spunta ispezione | Dichiara quale controllo viene eseguito da quella specifica vista. | Spuntare solo output realmente presenti nel ToolBlock. | Il validatore non richiede alla Rear, per esempio, una sigillatura non pubblicata. |
| 5 | Classi accettate | Elenco separato da virgola delle classi che valgono GOOD per quella vista. | Inserire le etichette esatte del modello VisionPro. | Una classe non compresa genera NOK quando l'AI e' abilitata. |
| 6 | Confidenza minima | Score minimo per accettare una classe GOOD, nella stessa scala pubblicata dal VPP. `0` disattiva il gate. | Impostare una soglia diversa per ogni vista usando campioni qualificati. | Se non e' chiaro se lo score usa scala `0..1` oppure `0..100`. |
| 7 | Salva | Scrive camera, matrice, classi e soglia nel file XML della ricetta attiva. | Fermare la macchina, attendere che non vi siano prodotti tracciati e collaudare campioni GOOD, NOK e sotto soglia. | Le altre ricette non vengono modificate. |
| 8 | Reset Default | Elimina il profilo esplicito della ricetta. | Usarlo solo per tornare consapevolmente ai default macchina. | Le ricette storiche e resettate ereditano la matrice macchina. |

Il mapping fisico I/O non e' contenuto nella ricetta e non cambia da questa
pagina. Scheda, canale, polarita', quote base e taratura encoder restano nella
configurazione macchina.

## Comportamento tra i prodotti

- la card si aggiorna alla chiusura dell'ispezione della relativa camera;
- immagine, classe, score e validazione provengono dallo stesso evento
  `UserResultAvailable`; un risultato in coda non puo' rileggere i valori del
  prodotto successivo;
- se il prodotto successivo non contiene un output di classificazione, la card
  viene nascosta e non conserva il risultato precedente;
- se l'ispezione `Classificazione AI` e' disabilitata, la card resta nascosta e
  classe/score non modificano esito, contatore, allarme o scarto;
- `Side` viene mostrata nella vista `Left` quando il job attivo ha ruolo Left;
- `Rear` viene mostrata nella vista `Right` quando il job attivo ha ruolo Right.

## Regola operativa

La classificazione e' una ispezione autonoma. Non incrementa i contatori
`SurfaceCheck`, `SideSealing` o altri controlli. Se piu' camere classificano NOK
lo stesso prodotto, il contatore `Classificazione AI` aumenta una sola volta.
La decisione di scarto segue esclusivamente il relativo flag di scarto ricetta.

Uno score sotto soglia e' un controllo eseguito ma non affidabile: il prodotto
e' classificato No Good e viene scartato quando lo scarto AI e' abilitato. Non
e' un errore software VisionPro. Un errore di esecuzione tool resta invece
`Non classificato` di processo e viene archiviato con codice generale `3`.

Il classificatore ONNX shadow della pagina `PC Diagnostics` e' un servizio
advisory separato: non compare in questa card a meno che il risultato venga
pubblicato dal job VisionPro tramite i terminali previsti.

## Esito archiviato per vista

Per ogni pezzo la HMI salva anche un esito numerico separato per ciascuna vista.
Questo dato serve alla tracciabilita' e al DataInspector; non aggiunge nuovi
comandi per l'operatore.

| ID | Valore | Significato | Verifica |
|---|---|---|---|
| 1 | `0` | Classificazione AI No Good; comprende classe `Unclassify` generata da score sotto soglia. | Confrontare classe originale nel dettaglio difetto, score, soglia e immagine. |
| 2 | `1` | Classificazione AI Good. | Il risultato della vista e' conforme rispetto all'AI. |
| 3 | `3` | Elaborazione VisionPro non classificabile per errore tool sulla vista. | Consultare log e immagini diagnostiche del pezzo. |
| 4 | `4` | Controllo AI disabilitato o non eseguito su quella vista. | E' normale per camere assenti o ricette senza AI. |

Le associazioni sono fisse: `Top3D` usa il campo Top, `Left` usa Side e `Right`
usa Rear. La presenza di `4` su una camera non installata non indica un guasto.
