# OPC UA Integration - 2026-05-26

## Scope

Questa integrazione aggiunge un client OPC UA alla HMI `QtisVisionPanel` per lo scambio dati con un server esterno, tipicamente PLC, SCADA o simulatore OPC UA.

Il contratto segue la baseline del progetto:

- `Config.xml` resta la configurazione macchina principale e non contiene sezioni OPC UA
- la configurazione OPC UA vive in un file separato: `OpcUaConfig.xml`
- la parametrizzazione runtime puo' essere sovrascritta da MySQL tramite `cfg_opcua_configuration`
- la macchina viene bloccata solo se OPC UA e' abilitato e marcato come obbligatorio

## File di configurazione

Il file viene creato automaticamente nella stessa cartella del `Config.xml` principale:

```text
C:\QtisVision\cfg\OpcUaConfig.xml
```

Valori principali:

```xml
<OpcUaConfig>
  <Enabled>false</Enabled>
  <RequiredForMachineRun>false</RequiredForMachineRun>
  <UseDatabaseConfiguration>true</UseDatabaseConfiguration>
  <ServerUrl>opc.tcp://127.0.0.1:4840</ServerUrl>
  <ApplicationName>QtisVisionPanel</ApplicationName>
  <UseSecurity>false</UseSecurity>
  <AutoAcceptUntrustedCertificates>false</AutoAcceptUntrustedCertificates>
  <RemoteCommandsEnabled>true</RemoteCommandsEnabled>
  <RemoteStartStopEnabled>true</RemoteStartStopEnabled>
  <RemoteRecipeChangeEnabled>true</RemoteRecipeChangeEnabled>
  <AuditRemoteCommandsToDatabase>true</AuditRemoteCommandsToDatabase>
  <ReconnectIntervalMs>5000</ReconnectIntervalMs>
  <SubscriptionIntervalMs>250</SubscriptionIntervalMs>
  <SessionTimeoutMs>60000</SessionTimeoutMs>
  <OperationTimeoutMs>5000</OperationTimeoutMs>
</OpcUaConfig>
```

Significato operativo:

- `Enabled=false`: il client OPC UA resta spento e la macchina lavora come prima
- `Enabled=true`: il client prova a connettersi al server configurato
- `RequiredForMachineRun=true`: lo start continuo VisionPro viene bloccato se il client OPC UA non e' connesso
- `UseDatabaseConfiguration=true`: dopo aver letto XML, la HMI applica i valori presenti in MySQL
- `RemoteCommandsEnabled=false`: la HMI continua a scambiare dati OPC UA, ma rifiuta tutti i comandi remoti con ACK negativo
- `RemoteStartStopEnabled=false`: Start/Stop remoti vengono rifiutati; il cambio ricetta remoto puo' restare abilitato
- `RemoteRecipeChangeEnabled=false`: cambio ricetta per nome o ID viene rifiutato; Start/Stop possono restare abilitati
- `AuditRemoteCommandsToDatabase=true`: comandi accettati o rifiutati vengono scritti anche in `audit_log`
- `AutoAcceptUntrustedCertificates=false`: default consigliato per configurazioni nuove; approvare/copiare i certificati server negli store PKI

## Tabella database

La tabella viene creata durante l'inizializzazione DB:

```sql
cfg_opcua_configuration
```

Campi principali:

- `ItemType`: `Setting` oppure `Node`
- `ItemKey`: nome logico del parametro o nodo
- `NodeId`: NodeId OPC UA, ad esempio `ns=2;s=QtisVision.Status.TotalCount`
- `Direction`: `ServerToClient` o `ClientToServer`
- `DataType`: `Boolean`, `String`, `Int64`, `Int32`, `Double`
- `Value`: valore testuale usato per i setting generali
- `Enabled`: abilita/disabilita singolo nodo
- `Required`: riservato per validazioni future sui nodi obbligatori
- `Description`: nota operativa

Le righe mancanti vengono inserite con `INSERT IGNORE`, quindi una modifica fatta dal manutentore in DB non viene sovrascritta al riavvio.

## Nodi default

Comandi da server verso HMI:

- `Start`: `ns=2;s=QtisVision.Commands.Start`, Boolean
- `Stop`: `ns=2;s=QtisVision.Commands.Stop`, Boolean
- `RecipeName`: `ns=2;s=QtisVision.Commands.RecipeName`, String
- `RecipeId`: `ns=2;s=QtisVision.Commands.RecipeId`, Int64, corrisponde a `tblproduzione.IdProduzione`
- `CommandId`: `ns=2;s=QtisVision.Commands.CommandId`, Int64, sequenza comando scritta per ultima dal server

Stati da HMI verso server:

- `TotalCount`: totale ispezioni
- `GoodCount`: pezzi conformi
- `BadCount`: pezzi non conformi
- `LastResult`: `true` se ultimo pezzo OK
- `CurrentRecipe`: ricetta caricata in produzione, senza estensione
- `CurrentRecipeId`: `tblproduzione.IdProduzione` della ricetta caricata in produzione
- `IsRunning`: stato run continuo VisionPro
- `Heartbeat`: contatore incrementale di comunicazione

Handshake comandi da HMI verso server:

- `CommandAckId`: ultimo `CommandId` processato dalla HMI
- `CommandAckOk`: `true` se il comando e' stato completato correttamente
- `CommandAckMessage`: messaggio di esito leggibile
- `CommandAckTimestamp`: timestamp ISO-8601 dell'ACK

Handshake dati da HMI verso server:

- `DataSequence`: sequenza snapshot dati pubblicata dalla HMI
- `DataPublishOk`: `true` se la HMI non ha rilevato errori nelle write OPC UA dello snapshot
- `DataPublishMessage`: messaggio di pubblicazione snapshot
- `DataPublishTimestamp`: timestamp ISO-8601 dello snapshot

Handshake dati da server verso HMI:

- `DataAckSequence`: sequenza `DataSequence` ricevuta/consumata dal server
- `DataAckOk`: `true` se il server ha accettato i dati
- `DataAckMessage`: messaggio server sull'esito della ricezione dati

## Flusso runtime

All'avvio:

1. la HMI carica `Config.xml`
2. durante l'inizializzazione DB crea `cfg_opcua_configuration`
3. carica o crea `OpcUaConfig.xml`
4. inserisce eventuali righe default mancanti in DB
5. se `UseDatabaseConfiguration=true`, applica i valori DB sopra quelli XML
6. se `Enabled=true`, apre una sessione OPC UA e sottoscrive i nodi comando

Durante produzione:

- dopo ogni ispezione, la HMI pubblica contatori, esito ultimo pezzo e ricetta corrente
- Start/Stop ricevuti da OPC UA comandano lo stesso runtime centralizzato usato dai pulsanti HMI
- una ricetta ricevuta da `RecipeName` viene normalizzata con estensione `.vpp`, salvata come `LastRecipe` e caricata con rollback verso la ricetta precedente in caso di errore
- una ricetta ricevuta da `RecipeId` viene risolta su MySQL tramite `tblproduzione.IdProduzione -> Ricetta` e poi caricata con lo stesso flusso del cambio per nome
- prima di eseguire un comando remoto, la HMI verifica i flag `RemoteCommandsEnabled`, `RemoteStartStopEnabled` e `RemoteRecipeChangeEnabled`
- se un comando e' disabilitato dalla policy, la HMI non lo esegue, scrive `CommandAckOk=false`, aggiorna `CommandAckMessage` e registra `OPCUA_COMMAND_REJECTED_BY_POLICY`

## Notifica operatore su cambio ricetta remoto

Quando il cambio ricetta OPC UA viene completato con successo:

1. la HMI scrive l'ACK comando verso OPC UA (`CommandAckId`, `CommandAckOk`, `CommandAckMessage`, `CommandAckTimestamp`)
2. aggiorna `CurrentRecipe` e `CurrentRecipeId`
3. riallinea il Recipe Manager, cosi' la ricetta in produzione resta in cima alla lista
4. mostra un popup informativo all'operatore con pulsante `ACKNOWLEDGE`
5. se nessun operatore conferma, il popup si chiude automaticamente dopo 1 minuto
6. la barra notifiche HMI resta aggiornata indicando che la ricetta in corsa e' stata cambiata da MES / OPC UA

Il popup e' solo informativo e non blocca `RunContinuous`.

## Logica handshake

### Comandi server -> HMI

Il server deve scrivere prima il payload comando e solo alla fine incrementare `CommandId`.

Esempio Start:

1. server scrive `Start=true`
2. server incrementa `CommandId`, ad esempio da `101` a `102`
3. la HMI riceve il nuovo `CommandId`
4. la HMI esegue Start tramite `MachineRuntimeService`
5. la HMI scrive:
   - `CommandAckOk=true` se `RunContinuous` risulta attivo
   - `CommandAckMessage=Start command completed: continuous run active.`
   - `CommandAckTimestamp=<timestamp>`
   - `CommandAckId=102`
6. il server considera il comando concluso solo quando `CommandAckId == CommandId`

Esempio Stop:

1. server scrive `Stop=true`
2. server incrementa `CommandId`
3. la HMI ferma il run continuo e mette hold manuale runtime
4. la HMI scrive `CommandAckId` uguale al comando e `CommandAckOk=true` solo se il run risulta inattivo

Esempio cambio ricetta per nome:

1. server scrive `RecipeName=Tovaglioli_celtex`
2. server incrementa `CommandId`
3. la HMI normalizza in `Tovaglioli_celtex.vpp`
4. la HMI salva `LastRecipe`, ricarica ricetta e runtime
5. se il cambio fallisce, la HMI tenta rollback alla ricetta precedente
6. la HMI scrive `CommandAckOk` e `CommandAckMessage` con esito finale

Esempio cambio ricetta per ID database:

1. server scrive `RecipeId=123`
2. server incrementa `CommandId`
3. la HMI cerca `tblproduzione.Ricetta` dove `IdProduzione=123` e `IsDeleted=false`
4. se la ricetta esiste, la HMI normalizza il nome e usa lo stesso caricamento del cambio ricetta per nome
5. se l'ID non esiste o il caricamento fallisce, la HMI scrive `CommandAckOk=false` e il motivo in `CommandAckMessage`
6. a cambio completato, la HMI aggiorna `CurrentRecipe` e `CurrentRecipeId`

Note:

- `CommandId` deve essere monotono crescente
- se il server usa i vecchi booleani senza `CommandId`, la HMI li accetta ancora, ma l'ACK avra' `CommandAckId=0`
- per un flusso robusto usare sempre `CommandId`
- usare `RecipeName` oppure `RecipeId` per lo stesso comando; se entrambi vengono valorizzati insieme, il nome ricetta ha priorita'
- dopo aver ricevuto ACK, il server puo' riportare `Start`/`Stop` a `false`
- se il comando viene rifiutato dalla policy OPC UA, il server deve trattare `CommandAckOk=false` come esito definitivo e leggere `CommandAckMessage`

### Dati HMI -> server

Dopo ogni ispezione la HMI pubblica uno snapshot dati:

1. `LastResult`
2. `TotalCount`
3. `GoodCount`
4. `BadCount`
5. `CurrentRecipe`
6. `CurrentRecipeId`
7. `Heartbeat`
8. `DataPublishOk`
9. `DataPublishMessage`
10. `DataPublishTimestamp`
11. `DataSequence`

`DataSequence` viene scritto per ultimo: lato server e' il segnale che lo snapshot e' completo.

Il server deve poi confermare:

1. legge tutti i valori dello snapshot
2. scrive `DataAckOk`
3. scrive `DataAckMessage`
4. scrive `DataAckSequence = DataSequence`

La HMI logga l'ACK dati quando vede cambiare `DataAckSequence`.

Regola pratica lato server:

- se `DataAckSequence == DataSequence`, lo snapshot e' stato consumato
- se `DataAckOk=false`, leggere `DataAckMessage`
- se `DataAckSequence` resta indietro, il server non sta confermando lo scambio dati

## Blocco macchina quando OPC UA e' vincolante

Lo start continuo viene permesso se almeno una condizione e' vera:

- `Enabled=false`
- `RequiredForMachineRun=false`
- sessione OPC UA connessa

Se `Enabled=true` e `RequiredForMachineRun=true`, ma la sessione non e' connessa, `MachineRuntimeService` blocca lo start, registra `OPCUA_RUN_BLOCKED` e marca il sottosistema visione come non sano.

Lo stop resta sempre consentito.

## Logging e storico eventi

Gli eventi OPC UA vengono scritti sia nel log tecnico NLog sia nello storico operativo `EventAudit`/DB tramite `ApplicationEventLogger`.

Eventi principali tracciati:

- `OPCUA_CONFIGURATION_LOADED`: configurazione OPC UA caricata; indica se `Enabled` e `RequiredForMachineRun` sono attivi
- `OPCUA_DISABLED`: OPC UA non usato; la macchina lavora senza tentare connessione
- `OPCUA_CONNECT_REQUESTED`: tentativo di connessione al server OPC UA, limitato a massimo un log al minuto durante riconnessioni ripetute
- `OPCUA_CONNECTED`: sessione OPC UA connessa
- `OPCUA_CONNECT_FAILED`: connessione fallita; `Warn` se OPC e' opzionale, `Error` se `RequiredForMachineRun=true`
- `OPCUA_DISCONNECTED`: disconnessione del client OPC UA
- `OPCUA_KEEPALIVE_FAILED`: keepalive sessione fallito
- `OPCUA_SUBSCRIPTION_CREATED`: subscription creata sui nodi server->HMI
- `OPCUA_SUBSCRIPTION_NODE_SKIPPED`: un nodo configurato non e' stato sottoscritto
- `OPCUA_SECURITY_POSTURE_WARNING`: configurazione potenzialmente debole, ad esempio certificati non trusted accettati automaticamente o comandi remoti con security disabilitata
- `OPCUA_COMMAND_ID_RECEIVED`: ricevuto nuovo `CommandId`
- `OPCUA_COMMAND_RECEIVED`: comando ricevuto (`Start`, `Stop`, `RecipeName`, `RecipeId`)
- `OPCUA_COMMAND_REJECTED_BY_POLICY`: comando remoto rifiutato dai flag di policy OPC UA e confermato con ACK negativo
- `OPCUA_COMMAND_ACK_OK` / `OPCUA_COMMAND_ACK_FAILED`: ACK scritto dalla HMI dopo esito reale del comando
- `OPCUA_DATA_ACK_RECEIVED` / `OPCUA_DATA_ACK_FAILED`: ACK snapshot dati ricevuto dal server
- `OPCUA_WRITE_FAILED` / `OPCUA_WRITE_EXCEPTION`: errore scrittura nodo HMI->server
- `OPCUA_DATA_SNAPSHOT_FAILED`: snapshot dati scritto con uno o piu' errori

Quando `AuditRemoteCommandsToDatabase=true`, i comandi remoti accettati o rifiutati vengono scritti anche in `audit_log` con comando, valore, `CommandId` e origine.

Per evitare crescita eccessiva dello storico in produzione, gli snapshot dati riusciti non vengono loggati a ogni pezzo. Restano comunque visibili tramite `DataSequence`, `DataPublishOk`, `DataPublishMessage` e `DataPublishTimestamp` sul server OPC UA.

## Pagina HMI configurazione OPC UA

La HMI espone una pagina tecnica `OPC UA` nell'area impostazioni macchina. L'accesso e' riservato a ruoli tecnici (`Expert`, `Installer`, `Administrator`) tramite feature `opcUaConfigurationView`.

La pagina permette di:

- vedere e modificare `Enabled`, `RequiredForMachineRun`, `UseDatabaseConfiguration`, security, URL server, application name e timeout
- abilitare/disabilitare comandi remoti in modo separato: tutti i comandi, solo Start/Stop, solo cambio ricetta, audit DB
- vedere tutti i nodi/tag scambiati con `NodeId`, direzione, tipo dato, abilitazione, required e descrizione
- visualizzare un diagramma direzionale con flusso `Server -> HMI` per comandi e `HMI -> Server` per stati/ACK
- salvare su `OpcUaConfig.xml` e su `cfg_opcua_configuration`
- ricaricare il client OPC UA dopo il salvataggio
- forzare reconnect o disconnect per collaudo

Nota operativa: se si abilita `RequiredForMachineRun`, la pagina chiede conferma per ricordare che RunContinuous puo' essere bloccato quando OPC UA e' disconnesso.

## Test con simulatore

Test rapido consigliato con UaExpert, Prosys OPC UA Simulation Server o server PLC reale:

1. avviare il server OPC UA
2. creare i nodi con i `NodeId` configurati in XML/DB
3. impostare in `OpcUaConfig.xml`:

```xml
<Enabled>true</Enabled>
<RequiredForMachineRun>false</RequiredForMachineRun>
<ServerUrl>opc.tcp://127.0.0.1:4840</ServerUrl>
<UseSecurity>false</UseSecurity>
<RemoteCommandsEnabled>true</RemoteCommandsEnabled>
<RemoteStartStopEnabled>true</RemoteStartStopEnabled>
<RemoteRecipeChangeEnabled>true</RemoteRecipeChangeEnabled>
```

4. avviare la HMI e verificare nei log `OPC UA connected`
5. mandare `Start=true` dal server e verificare avvio run continuo
6. mandare `Stop=true` e verificare stop/hold runtime
7. scrivere `RecipeName=Tovaglioli_celtex` e verificare caricamento ricetta `Tovaglioli_celtex.vpp`
8. scrivere `RecipeId=<IdProduzione valido>` e verificare caricamento della ricetta associata in `tblproduzione`
9. generare ispezioni e verificare aggiornamento di `TotalCount`, `GoodCount`, `BadCount`, `LastResult`, `CurrentRecipe`, `CurrentRecipeId`, `IsRunning`, `Heartbeat`, `DataSequence`
10. lato server scrivere `DataAckOk=true`, `DataAckMessage=OK`, `DataAckSequence=<DataSequence>`
11. verificare nei log HMI `OPCUA_DATA_ACK_RECEIVED`

Test handshake comando:

1. server scrive `Start=true`
2. server incrementa `CommandId`
3. verificare che HMI scriva `CommandAckId=<CommandId>`
4. verificare `CommandAckOk=true`
5. verificare `CommandAckMessage`

Test policy comandi remoti:

1. impostare `RemoteCommandsEnabled=false`
2. mandare un comando remoto, ad esempio `Start=true` + nuovo `CommandId`
3. verificare che la macchina non parta
4. verificare `CommandAckOk=false`
5. verificare `CommandAckMessage` con motivo di rifiuto
6. verificare nei log/audit `OPCUA_COMMAND_REJECTED_BY_POLICY`
7. riabilitare `RemoteCommandsEnabled=true` prima del collaudo normale

Test vincolante:

1. impostare `Enabled=true`
2. impostare `RequiredForMachineRun=true`
3. spegnere il server OPC UA
4. premere Start in HMI
5. risultato atteso: run continuo non parte e nei log compare `OPCUA_RUN_BLOCKED`
6. riaccendere il server OPC UA
7. dopo riconnessione, Start deve essere nuovamente permesso

## Note di commissioning

- in produzione lasciare `AutoAcceptUntrustedCertificates=false` se si usa security con certificati gia' distribuiti
- per il primo test su banco si puo' usare `UseSecurity=false`
- prima di collegare MES/SCADA reale decidere se il server puo' comandare Start/Stop o cambio ricetta; in caso contrario lasciare attivo solo lo scambio dati e disabilitare i flag remoti dedicati
- il server deve esporre nodi scrivibili per gli stati HMI e nodi leggibili/sottoscrivibili per i comandi
- se si modifica la tabella DB, riavviare la HMI per ricaricare la configurazione OPC UA
- se si vuole tornare al comportamento storico, impostare `Enabled=false`

## Certificati OPC UA e Ignition

Il client crea automaticamente la struttura PKI sotto la cartella runtime dell'applicazione:

- `OPC\pki\own`: certificato applicazione della HMI
- `OPC\pki\trusted`: certificati server/peer considerati trusted
- `OPC\pki\issuers`: certificati CA/issuer trusted
- `OPC\pki\rejected`: certificati rifiutati durante il primo contatto
- `OPC\pki\trusted_user`: certificati utente trusted, se usati dal server
- `OPC\pki\user_issuers`: issuer dei certificati utente

Server strict come Ignition richiedono che anche `TrustedIssuerCertificates` abbia uno `StorePath` valido; in caso contrario la connessione fallisce con `BadConfigurationError` prima della negoziazione endpoint.

Per test con Ignition:

1. configurare `ServerUrl` verso l'endpoint Ignition corretto
2. se il server permette endpoint senza security, usare `UseSecurity=false` per il primo test banco
3. se si usa security, avviare una prima connessione dalla HMI per generare il certificato client in `OPC\pki\own`
4. approvare nel gateway Ignition il certificato client generato dalla HMI
5. se `AutoAcceptUntrustedCertificates=false`, copiare il certificato server o la CA negli store `trusted` / `issuers`
6. verificare nello storico operativo `OPCUA_CONNECT_REQUESTED`, poi `OPCUA_CONNECTED` oppure il dettaglio `OPCUA_CONNECT_FAILED`

Se Ignition espone solo endpoint sicuri, la HMI deve avere un certificato applicazione client valido per la security policy scelta, ad esempio `Basic256Sha256`. Il client lo crea automaticamente nello store `own`; se la creazione fallisce, il log indica esplicitamente il percorso dove non e' stato possibile creare o caricare il certificato.
