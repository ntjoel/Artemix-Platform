# Richiesta Task per `QtisVisionPanel`

> Prima di iniziare, leggere `AGENTS.md`.
> Se il task e' ampio, usare sub-agent solo per analisi locali, verifiche mirate o parallelizzazione di attivita' ben delimitate.
> Non delegare ai sub-agent decisioni architetturali, modifiche al runtime macchina, ne' cambi che possano rompere compatibilita' di ricette, XML o DB.

## Obiettivo
Descrivi in modo sintetico cosa vuoi ottenere e quale problema deve risolvere.

## Contesto
Indica:
- area del progetto coinvolta
- comportamento attuale
- comportamento atteso
- eventuali riferimenti a schermate, flussi, classi o documenti

## Perimetro della modifica
Specifica chiaramente:
- file / moduli da toccare
- file / moduli da non toccare
- eventuali dipendenze esterne o vincoli tecnici

## Impatto atteso
Indica esplicitamente se il task impatta:
- Ricetta
- XML
- DB
- Runtime
- Documentazione

Per ciascun punto, descrivi brevemente l'effetto previsto.

## Rischio e compatibilita'
Classifica il change:
- Low risk
- Medium risk
- High risk

Aggiungi:
- motivazione del rischio
- compatibilita' con ricette esistenti
- necessita' di migrazione o aggiornamento dati
- eventuali regressioni possibili

## Sub-agent
Se vuoi che vengano usati sub-agent, specifica:
- obiettivo del sub-agent
- perimetro di analisi
- output atteso
- vincoli di non regressione

Se non servono, scrivi: `Nessun sub-agent necessario`.

## Criteri di accettazione
Elenca cosa deve essere vero per considerare il task completato, per esempio:
- comportamento verificato
- nessuna regressione su ricette/XML/DB
- documentazione aggiornata se necessario
- test eseguiti e risultati attesi rispettati

## Test e verifica
Indica come vuoi che venga validata la modifica:
- test manuali
- test automatici
- verifica runtime
- controllo serializzazione / persistenza
- controllo documentazione

## Note aggiuntive
Aggiungi eventuali priorita', scadenze, vincoli operativi o riferimenti utili.
