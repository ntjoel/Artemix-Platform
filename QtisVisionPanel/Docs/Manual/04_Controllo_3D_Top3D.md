---
title: 04 Controllo 3D Top3D
description: Procedura operatore per visualizzare immagini, misure e stati del controllo 3D sul pannello.
image: Images/02_camera_monitor.png
image_caption: Vista overview con superficie Top3D, vista planare Top2D e riepilogo misure.
---

## Vista operatore Top3D

![Vista Overview usata per Top3D e Top2D](Images/02_camera_monitor.png)

| ID | Area | Cosa mostra | Azione operatore | Quando chiamare supporto |
|---|---|---|---|---|
| 1 | Top3D Sensor Features | Immagine 3D principale, altezza con icona `Z` e profilo con icona curva `3D`. | Controllare immagine aggiornata, icona e badge. | Se la vista e vuota o non cambia. |
| 2 | Top2D Planar Features | Vista planare con larghezza identificata da `X` e lunghezza identificata da `Y`. | Verificare presenza della seconda vista e delle due misure. | Se compare solo Top3D. |
| 3 | Measured | Valore misurato in millimetri. | Confrontare con il target visualizzato. | Se il valore e fisicamente impossibile. |
| 4 | Target | Nominale e tolleranza della ricetta. | Verificare la ricetta prima di correggere. | Se non coincide con la scheda prodotto. |
| 5 | GOOD / NO GOOD / WAIT | Esito corrente della singola misura. | Attendere il pezzo successivo se `WAIT`. | Se resta `WAIT` durante produzione. |
| 6 | Misura corretta / Offset | Valore compensato e correzione globale attiva, visibili solo quando l'offset e diverso da zero. | Verificare che il segno della correzione sia quello atteso. | Se la correzione richiesta supera il normale errore di calibrazione. |

## Scopo
Questa procedura serve per verificare che il controllo 3D sia visualizzato correttamente sul pannello e che l'operatore possa leggere subito:

- immagine principale `Top3D`
- immagine secondaria `Top2D`
- misura altezza
- misura larghezza
- misura lunghezza
- stato `GOOD / NO GOOD`

## Dove guardare nel pannello
Aprire la vista `Overview`.

Su macchina `3DCheck` con job semantico `Top3D`, la schermata mostra:

- riquadro alto `Top3D Sensor Features`
  - immagine principale del controllo 3D
  - scheda misura altezza con valore misurato e stato
- riquadro secondario `Top2D Planar Features`
  - immagine planare usata per larghezza e lunghezza
  - due schede misura:
    - larghezza
    - lunghezza

Se il runtime ha un solo job reale `Top3D`, il pannello crea automaticamente una seconda vista virtuale `Top2D` per l'operatore.

### Icone di classificazione

Le icone permettono di riconoscere subito la misura senza dipendere dal testo o dalla
posizione della scheda.

| Icona | Controllo identificato | Area del pannello |
|---|---|---|
| Cubo con asse `Z` | Altezza 3D | `Top3D Sensor Features` |
| Profilo curvo con punti `3D` | Regolarita del profilo e qualita della superficie acquisita | `Top3D Sensor Features` |
| Cubo con asse `X` | Larghezza 3D | `Top2D Planar Features` |
| Cubo con asse `Y` | Lunghezza 3D | `Top2D Planar Features` |

L'icona identifica il tipo di ispezione; non sostituisce il risultato. Il badge accanto
alla misura resta l'indicazione autorevole:

- `WAIT`: misura non ancora disponibile;
- `GOOD`: misura valida e in tolleranza;
- `NO GOOD`: misura valida ma fuori tolleranza;
- `INVALID` o `MONITOR`, quando previsti dal controllo profilo, indicano rispettivamente
  dato non valido o sola sorveglianza senza scarto fisico.

## Prima di avviare il controllo 3D
Verificare in `Recipe Management`:

1. che la macchina sia di tipo `3DCheck`
2. che la ricetta corretta sia selezionata
3. che nella sezione `Product properties` siano compilati:
   - altezza
   - larghezza
   - lunghezza
   - profondita se richiesta
4. che nella sezione `Top3D profilometer checks` siano compilati:
   - tolleranza altezza
   - tolleranza larghezza
   - tolleranza lunghezza

I nominali altezza, larghezza e lunghezza Top3D sono mostrati in sola lettura e seguono
automaticamente i corrispondenti valori di `Product properties`. Salvare la ricetta dopo aver
modificato le dimensioni prodotto; il salvataggio persiste gli stessi nominali nella sezione Top3D.

## Come configurare le immagini da visualizzare
La vista 3D puo leggere i percorsi immagine in due modi.

### Modalita consigliata: per ricetta
Nella sezione `Top3D profilometer checks` compilare:

- `Top3D primary view`
- `Top2D secondary view`

Esempi tipici:

- `Measure.Height.InputImage`
- `Measure.CogIPOneImageTool1.OutputImage`
- `Measure.CogPixelMapTool1.OutputImage`

Il pannello accetta sia il path completo visto in QuickBuild sia il path relativo al
record `LastRun`. Quindi questi esempi sono equivalenti quando puntano allo stesso
record immagine:

- `Measure.Height.InputImage`
- `Height.InputImage`
- `LastRun.Height.InputImage`

Usare questa modalita quando la stringa VisionPro cambia in base al prodotto o alla tipologia macchina.

### Modalita fallback: Config.xml
Se i due campi ricetta sono lasciati vuoti, il pannello usa automaticamente:

- `Config.xml -> LasRunParam.LastRunView1` per `Top3D`
- `Config.xml -> LasRunParam.LastRunView2` per `Top2D`

Questa modalita e utile quando tutte le ricette della macchina condividono le stesse stringhe di visualizzazione.

## Procedura operatore per vedere le immagini 3D
1. Caricare la ricetta corretta.
2. Salvare la ricetta se sono stati modificati nominali, tolleranze o viste immagine.
3. Avviare la produzione.
4. Aprire `Overview`.
5. Controllare che il riquadro `Top3D` mostri un'immagine aggiornata.
6. Controllare che il riquadro `Top2D` mostri un'immagine aggiornata.
7. Leggere le schede misura:
   - altezza in `Top3D`
   - larghezza e lunghezza in `Top2D`
8. Controllare il badge di stato:
   - `GOOD` se la misura e in tolleranza
   - `NO GOOD` se la misura e fuori tolleranza
   - `WAIT` se non e ancora disponibile un valore valido

## Come interpretare le misure
Ogni scheda mostra:

- `Measured`: valore VisionPro quando la correzione globale e zero
- `Misura corretta`: valore VisionPro piu offset quando una correzione e attiva
- `Offset`: correzione globale di calibrazione applicata, con segno positivo o negativo
- `Target`: nominale con tolleranza
- badge finale di esito

Esempio:

- `Measured: 15.220 mm`
- `Target: 15.000 +/- 0.300 mm`
- `GOOD`

Significato:

- `GOOD`: il valore e dentro l'intervallo accettato
- `NO GOOD`: il valore e fuori tolleranza e puo generare difetto/scarto
- `WAIT`: il pannello non ha ancora ricevuto una misura valida

## Correggere le tre misure dopo il collaudo

La correzione e una taratura globale della macchina, quindi vale per tutte le ricette. Nominali e
tolleranze prodotto restano nella ricetta e non vengono modificati.

Accesso richiesto: `Installer` o `Administrator`.

1. Fermare la produzione e usare un campione o riferimento dimensionale attendibile.
2. Eseguire piu misure senza offset e calcolare la media VisionPro per altezza, larghezza e lunghezza.
3. Calcolare ogni correzione con:
   `offset = misura di riferimento - media VisionPro`.
4. Aprire `System Preferences`.
5. Aprire l'editor `Config.xml` e raggiungere `VisionPro outputs`.
6. Nella sezione `Correzione misure Top3D` inserire:
   - `Offset altezza`
   - `Offset larghezza`
   - `Offset lunghezza`
7. Premere `Salva configurazione`.
8. Ripetere la misura sul riferimento e verificare il valore corretto in `Overview`.

I campi accettano sia il punto sia la virgola come separatore decimale.

Esempio: il riferimento altezza e `250,000 mm` e VisionPro misura in media `248,700 mm`.
Inserire `+1,300 mm`. Se VisionPro misura invece `251,100 mm`, inserire `-1,100 mm`.

La formula usata dal runtime e:

`misura corretta = misura grezza VisionPro + offset globale`

Gli offset accettati sono compresi tra `-1000` e `+1000 mm`. Un `Config.xml` precedente, privo dei
tre campi, equivale automaticamente a tre offset uguali a zero. Il database continua a usare le
colonne misura esistenti e archivia il valore corretto effettivamente validato.

## Se si vede solo Top3D e non Top2D
Verificare nell'ordine:

1. che il job attivo si chiami semanticamente `Top3D`
2. che la macchina sia configurata come `3DCheck`
3. che la ricetta sia salvata dopo la modifica delle viste
4. che `Top2D secondary view` non punti a una stringa vuota o errata
5. se il campo ricetta e vuoto, verificare `Config.xml -> LasRunParam.LastRunView2`

Se il pannello continua a mostrare solo la vista principale, aprire `Events Monitor` e leggere gli ultimi messaggi VisionPro / runtime.

## Se le immagini non sono leggibili o non cambiano
Controllare:

1. che VisionPro sia in esecuzione regolare
2. che il prodotto passi davvero sotto il sensore
3. che la ricetta caricata sia quella corretta
4. che i path `Top3D primary view` e `Top2D secondary view` corrispondano a record realmente esposti dal job
5. se appare `DISPLAY_RECORD_PATH_NOT_FOUND`, correggere il path del record immagine in ricetta
6. i warning `Top3D artifact export skipped` riguardano solo il salvataggio opzionale dei file 3D di supporto e non impediscono la visualizzazione in `Overview`

## Quando chiamare il supporto
Prima di chiedere assistenza annotare:

- nome ricetta
- orario preciso
- immagine visibile in `Top3D`
- immagine visibile in `Top2D`
- misura mostrata come `NO GOOD`
- ultimo messaggio evento VisionPro

Se possibile allegare uno screenshot della schermata `Overview` con entrambe le viste.

## Controllo profilo superiore Top3D

Il controllo profilo confronta la parte alta della superficie con una soglia ricetta. In questa
prima release e un controllo di processo: registra un'anomalia e aggiorna storico e contatori, ma
non comanda lo scarto fisico.

| ID | Campo o stato | Significato | Azione operatore |
|---|---|---|---|
| 7 | Profilo Top3D | Scheda separata dalla misura dimensionale altezza. | Verificare lo stato dopo ogni acquisizione. |
| 8 | Altezza mediana | Quota centrale e stabile della ROI, usata come altezza ufficiale quando disponibile. | Confrontarla con il valore fisico del campione. |
| 9 | High Tail | Quota della parte alta della distribuzione. | Usarla solo come informazione diagnostica. |
| 10 | Indice pancia | Differenza `High Tail - Mediana` sull'intera ROI. | Confrontarla con il limite ricetta. |
| 11 | Pixel validi | Percentuale di pixel 3D realmente utilizzabili. | Se bassa, pulire ottica e verificare posizione prodotto. |
| 12 | Massimo | Massimo assoluto VisionPro. | Usarlo solo per diagnostica, mai come soglia profilo. |
| 13 | Stato | `GOOD`, `NO GOOD`, `INVALID`, `MONITOR` o `WAIT`. | Seguire la tabella stati seguente. |

### Impostare il controllo nella ricetta

Accesso richiesto: ruolo autorizzato alla modifica ricetta/tolleranze.

1. Aprire `Recipe Management`.
2. Selezionare la ricetta del prodotto.
3. Aprire `Top3D profilometer checks`.
4. Abilitare `Profilo Top3D`.
5. Inserire `Pixel validi minimi` in percentuale, per esempio `75`.
6. Inserire `Indice pancia massimo` in millimetri.
7. Salvare la ricetta.
8. Eseguire campioni noti GOOD e NOK e controllare la scheda in `Overview`.

I campi accettano punto o virgola. `Pixel validi minimi` deve essere tra `0` e `100`; l'indice
pancia massimo non puo essere negativo. Un limite pancia uguale a `0` mantiene il profilo in sola
osservazione, senza generare un esito profilo.

`Indice pancia = High Tail - Mediana` sull'intera ROI. Non indica dove si trova il difetto e non ne
misura la lunghezza.

### Interpretare gli stati

| Stato | Significato | Cosa fare |
|---|---|---|
| `GOOD` | Profilo abilitato, acquisizione valida e indice pancia entro soglia. | Nessuna azione. |
| `NO GOOD` | Acquisizione valida, ma indice pancia oltre il limite ricetta. | Verificare il prodotto; il pannello registra l'evento ma non scarta automaticamente. |
| `INVALID` | Pixel validi insufficienti, output assente o valore numerico non valido. | Controllare sensore, ROI, luce e output VisionPro. |
| `MONITOR` | I valori sono disponibili, ma controllo o soglia non sono attivi. | Abilitare e configurare solo dopo il collaudo prodotto. |
| `WAIT` | Nessun risultato profilo disponibile. | Attendere il pezzo; se resta fisso, controllare VisionPro. |

### Compatibilita con ricette e job precedenti

- Una vecchia ricetta apre il controllo disabilitato e conserva il comportamento precedente.
- Se `HeightMedian` non e disponibile, l'altezza usa ancora l'output legacy `ThreeDHeight`.
- L'offset globale altezza si applica solo all'altezza ufficiale; non modifica indice pancia o
  percentuale pixel validi.
- Un vecchio job senza i nuovi output non produce falsi difetti quando il profilo e disabilitato.

### Verifica nel Data Inspector

Per un pezzo segnalato aprire `DataInspector` e selezionare il record. Nei dettagli Top3D sono
riportati mediana, High Tail, massimo diagnostico, indice pancia, pixel validi, soglie ricetta e
stato. Il badge `Segnalato` indica un controllo di processo NOK senza espulsione fisica; `Scartato`
resta riservato ai pezzi per cui il runtime ha comandato lo scarto.
