# Machine Runtime Foundation

## Obiettivo del primo blocco

Questo blocco introduce la base runtime sopra l'hardware Advantech gia' esistente, senza spostare la logica macchina dentro `MainWindow` o dentro il solo `DigitalIOViewModel`.

Componenti introdotti:

- `MachineRuntimeConfiguration`: file configurazione macchina persistente
- `MachineConfigurationService`: load/save XML in `cfg/machine_runtime_config.xml`
- `TrackedProduct`: record runtime del prodotto tracciato
- `MachineController`: base tracking prodotto usando fotocellula + encoder

## Cosa fa oggi

- salva e ricarica la mappatura macchina dalla schermata hardware
- crea un prodotto tracciato al fronte attivo della fotocellula configurata
- usa l'encoder principale per calcolare quota trigger e quota scarto
- mantiene una coda runtime prodotti
- segnala quando un prodotto NOK arriva al punto di scarto

## Cosa non fa ancora

- non comanda ancora realmente illuminatori, trigger e scarto
- non aggancia ancora l'esito Cognex a `TrackedProduct`
- non gestisce ancora multi-camera con identificazione prodotto completa
- non centralizza ancora l'accesso hardware condiviso tra tutte le viewmodel

## Prossimo blocco consigliato

1. Agganciare l'esito ispezione Cognex al `TrackedProduct`
2. Pilotare le uscite macchina tramite `MachineController`
3. Unificare l'accesso hardware per evitare piu' `AdvantechDeviceManager` in parallelo
