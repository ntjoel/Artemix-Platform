# Counter Defect Single-Source Flow - 2026-07-01

## Scopo

Questa nota descrive il flusso corretto dei contatori produzione e dei contatori difetto dopo la release `3.0.2.4`.

Il problema corretto era il doppio incremento dei contatori difetto: un pezzo NOK aumentava `NoGood` una volta, ma lo stesso difetto poteva aumentare due volte nella lista `Defects Counters`.

## Flusso runtime corretto

1. `MainWindow` apre il ciclo pezzo con `CounterManager.BeginInspectionCycle()`.
2. `CounterManager` pulisce i messaggi difetto correnti e azzera la memoria dei difetti gia conteggiati nel ciclo.
3. `DataManage/IToolBlockValidator.cs` valida i controlli VisionPro.
4. Quando un controllo fallisce, il validator chiama `CounterManager.IncrementDefectAsync(featureKey, message)`.
5. `IncrementDefectAsync` normalizza la chiave difetto, incrementa il contatore specifico solo se quella chiave non e' gia stata conteggiata nel ciclo, e salva il messaggio operatore corrente.
6. A fine ispezione, `MainWindow.ProcessInspectionWithCountersAsync()` chiama `CounterManager.ProcessInspectionAsync(isCompliant, defects)`.
7. `ProcessInspectionAsync` incrementa solo `Total`, `Good` o `NoGood`.
8. La UI viene aggiornata tramite `CountersUpdated`; il backup locale viene salvato e il flush MySQL resta demandato al flusher.

## Responsabilita

`CounterManager.ProcessInspectionAsync`:

- incrementa `Total`
- incrementa `Good` se il pezzo e' conforme
- incrementa `NoGood` se il pezzo non e' conforme
- pulisce i messaggi difetto quando arriva un pezzo conforme
- non incrementa contatori difetto specifici

`CounterManager.IncrementDefectAsync`:

- incrementa il contatore difetto specifico
- non incrementa due volte la stessa chiave difetto nello stesso ciclo pezzo
- aggiorna il messaggio rosso visibile nella pagina Counters
- mantiene la normalizzazione delle chiavi legacy, ad esempio `print_centering`, `shapetop`, `sidesealing`, `BottomSealing`

## Motivo della separazione

I validator sono l'unico punto che conosce con precisione quale controllo e' fallito. Il riepilogo finale dell'ispezione conosce invece solo l'esito pezzo e la mappa dei difetti gia rilevati.

Per questo motivo:

- i contatori produzione stanno nel path finale dell'ispezione
- i contatori difetto stanno nel path dei validator
- non deve esistere un secondo incremento difetti nel riepilogo finale
- nello stesso ciclo pezzo, la stessa famiglia difetto viene contata una volta sola anche se piu controlli generano messaggi della stessa chiave

## Impatti

- `Config.xml`: nessun cambio
- `machine_runtime_config.xml`: nessun cambio
- MySQL: nessuna nuova colonna o tabella
- Backup contatori: stesso formato JSON
- UI operatore: nessun nuovo comando

## Nota per collaudo

Dopo il deploy, se la macchina ha gia prodotto con il comportamento precedente, i valori storici dei difetti possono risultare gonfiati. Per validare la correzione:

1. Effettuare un reset contatori con utente autorizzato.
2. Passare un pezzo Good e verificare `Total +1`, `Good +1`, nessun difetto +1.
3. Passare un pezzo con un solo difetto noto e verificare `Total +1`, `NoGood +1`, difetto specifico +1.
4. Passare un pezzo con due difetti reali e verificare `Total +1`, `NoGood +1`, ciascun difetto reale +1.
