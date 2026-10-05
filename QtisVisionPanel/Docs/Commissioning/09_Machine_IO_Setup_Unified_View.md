# Machine Setup - Guida Commissioning

## Scopo

Questa pagina serve a configurare in modo piu chiaro la parte macchina legata a encoder, I/O e punti intervento:

- riferimento encoder e velocita linea
- calibrazione tachimetro encoder
- punti intervento
- uscite usate da ogni punto
- binding runtime
- tabelle input/output
- encoder
- backup configurazione

Percorso pannello:

`Configuration -> PCIE settings -> Machine Setup`

## Cosa Guardare Per Primo

| Sezione | Cosa controllare |
|---|---|
| Header alto | stato configurazione e pulsanti `Save config`, `Load config`, `Create backup` |
| Encoder setup | velocita linea, pezzi/min, zero macchina, fotocellula e calibrazione tachimetro |
| Runtime summary | fotocellula, trigger, scarto, allarme, heartbeat |
| Machine position sequence | ordine reale dei punti lungo la macchina |
| Intervention point to physical output | uscita fisica usata da ogni punto |
| Complete machine configuration | tabelle tecniche complete, aperte di default |

## Significato Delle Parti

| Parte | Significato |
|---|---|
| Intervention point | quota e azione da fare sul prodotto |
| Signal code | nome logico del segnale usato dal runtime |
| Board | scheda fisica, per esempio `PCIE-1756-BE` |
| Channel | canale fisico, per esempio `DO02` |
| Polarity | stato elettrico attivo alto/basso |
| Pulse ms | durata impulso del punto |

## Quota Globale E Offset Ricetta

La pagina `Machine Setup` contiene la geometria nominale della macchina, non la geometria di un
singolo prodotto.

| Campo | Livello | Uso corretto |
|---|---|---|
| `Base position mm` | macchina globale | distanza meccanica nominale tra PRODUCT_ZERO e camera |
| `Trim +/- mm` | macchina globale | correzione di commissioning valida per tutte le ricette |
| `Camera position offset` | ricetta | differenza positiva/negativa dovuta alle dimensioni del prodotto |

Formula applicata dal runtime:

```text
quota effettiva = base macchina + trim commissioning + offset ricetta
```

Non compensare un prodotto specifico modificando `Base position mm` o `Trim +/- mm`: la modifica
sposterebbe tutte le ricette. Usare `Recipe Management -> Trigger configuration -> Recipe camera
position corrections`.

## Procedura Consigliata

1. Aprire `Machine Setup`.
2. Verificare che `Encoder setup` mostri velocita e zero macchina coerenti.
3. Verificare che `Runtime summary` mostri i segnali attesi.
4. Controllare la sequenza in `Machine position sequence`.
5. Controllare la tabella `Intervention point to physical output`.
6. Se un punto usa un'uscita sbagliata, selezionare il punto nell'editor.
7. Modificare `Signal code` scegliendo un segnale esistente.
8. Se serve modificare il canale fisico, usare `Complete machine configuration`.
9. Entrare nella tab `Machine outputs`.
10. Correggere `Board`, `Channel`, `Polarity` o `Real signal`.
11. Premere `Save config`.
12. Creare un backup dopo una configurazione confermata.

## Modifica Machine Inputs / Outputs

Le tabelle `Machine inputs` e `Machine outputs` non sono state eliminate: sono dentro `Complete machine configuration`.
Anche `Additional runtime bindings` resta disponibile nella tab `Runtime binding`.

| Azione | Come fare |
|---|---|
| Modificare un segnale | cliccare nella cella e cambiare valore |
| Aggiungere un segnale | usare l'ultima riga vuota della tabella |
| Eliminare un segnale | selezionare la riga e cancellarla dal DataGrid |
| Applicare la modifica | premere `Save config` |
| Filtrare la tabella | usare i filtri rapidi `Only runtime`, `Only unassigned`, `Only real` sopra input/output |

Campi normalmente modificabili:

| Campo | Uso |
|---|---|
| `Signal code` | nome logico usato da runtime e punti intervento |
| `Machine function` | descrizione leggibile per manutentore |
| `Board` | scheda fisica, esempio `PCIE-1756-BE` |
| `Channel` | canale fisico, esempio `DI00` o `DO02` |
| `Category` | tipo funzione: camera, scarto, allarme, sensore |
| `Polarity` | `ActiveHigh` o `ActiveLow` |
| `Real signal` | indica se il segnale e' cablato sulla macchina reale |
| `Operational notes` | nota pratica per collaudo o manutenzione |

## Regola Da Ricordare

Il punto intervento decide quando fare l'azione.

La tabella output decide dove l'azione esce fisicamente.

La ricetta puo aggiungere solo una correzione geometrica al punto camera; non puo cambiare il
canale fisico.

Non cambiare entrambe le cose insieme senza annotare cosa si sta facendo.

## Controllo Rapido Trigger Camera

| Punto | Deve indicare |
|---|---|
| `CAMERA_TRIGGER_TOP` | uscita trigger camera top |
| `CAMERA_TRIGGER_SIDE` / `CAMERA_TRIGGER_LEFT` | uscita trigger side/left |
| `CAMERA_TRIGGER_RIGHT` / `CAMERA_TRIGGER_REAR` | uscita trigger right/rear |
| `CAMERA_TRIGGER_BOTTOM` | uscita trigger bottom |
| `REJECT` | uscita elettrovalvola scarto |

## Da Dove Arriva La Tabella Punto -> Uscita

La tabella `Intervention point to physical output` e' un riepilogo calcolato:

1. legge i punti da `InterventionPoints`
2. prende il `Signal code` del punto
3. cerca quel codice in `Machine outputs`
4. se non trova un output, cerca in `Machine inputs`
5. mostra `Board`, `Channel`, `Polarity` e stato del segnale trovato

Se `Board` o `Channel` sono vuoti significa normalmente:

| Caso | Cosa controllare |
|---|---|
| `Signal not found` | il `Signal code` del punto non esiste in `Machine outputs` / `Machine inputs` |
| Riga test duplicata | disattivare temporaneamente i filtri e controllare duplicati con lo stesso `Signal code` |
| Right/Rear | usare `OUT_CAMERA_REAR_TRIGGER` per la camera fisica Rear usata come Right, oppure verificare l'alias automatico |
| Left/Side | usare il segnale fisico effettivo previsto dalla macchina, `OUT_CAMERA_LEFT_TRIGGER` o `OUT_CAMERA_SIDE_TRIGGER` |

## Se Qualcosa Non Torna

| Sintomo | Controllo |
|---|---|
| Il punto e' giusto ma non esce nulla | controllare `Machine outputs -> Channel` |
| Esce il canale sbagliato | controllare `Signal code` nel punto e `Channel` in output |
| Il punto scatta troppo presto/tardi | controllare `Base position mm` e `Trim +/- mm` |
| Il MultiShot usa l'uscita sbagliata | controllare profilo MultiShot e output trigger associato |
| La macchina non segue l'encoder | controllare `Use real encoder trigger` e `Main encoder` |

Se il problema riguarda una sola ricetta, controllare prima l'offset camera della ricetta. Se
riguarda tutte le ricette, controllare base e trim macchina.

## MultiShot: Globale E Per Ricetta

| Resta globale macchina | Puo variare in ricetta |
|---|---|
| trigger output e canale | eredita / abilita / disabilita profilo |
| durata impulso | delta numero scatti |
| intervallo minimo tra impulsi | delta step in mm |
| direzione encoder e counts/mm | delta origine sequenza in mm |
| timeout, max shots e tolleranza ritardo | nessun parametro fisico aggiuntivo |
| calibrazione camera mm/px | nessuna calibrazione alternativa |

Prima configurare e collaudare il profilo globale. Solo dopo applicare i delta della ricetta e
verificare che `expectedFrames` e `stepMm` nel ToolBlock VisionPro abbiano i valori effettivi.

## Backup

Prima di modifiche importanti:

1. premere `Create backup`
2. fare la modifica
3. premere `Save config`
4. verificare con prova reale o simulazione
5. creare un nuovo backup se la prova e' OK
