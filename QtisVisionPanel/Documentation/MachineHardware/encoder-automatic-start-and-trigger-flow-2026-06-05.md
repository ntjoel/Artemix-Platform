# Encoder Automatic Start And Trigger Flow

## Obiettivo

In una macchina automatica il contatore encoder non deve dipendere dall'apertura della pagina I/O o dal pulsante manuale `Start`.

Dalla release `2.0.1.4`:

- i canali encoder configurati vengono armati automaticamente durante lo startup HMI;
- il valore corrente del contatore principale viene acquisito prima di accettare prodotti;
- la taratura tachimetrica salva automaticamente il nuovo `MillimetersPerRevolution`;
- quote trigger e scarto usano lo stesso moltiplicatore `TrackingMode` (`x1`, `x2`, `x4`) usato dalla velocita HMI.

## Configurazione persistente

La conversione macchina resta in:

```xml
<EncoderConfigurationTemplate>
  <PulsesPerRevolution>2048</PulsesPerRevolution>
  <MillimetersPerRevolution>...</MillimetersPerRevolution>
  <TrackingMode>Quadrature x4</TrackingMode>
</EncoderConfigurationTemplate>
```

Formula:

```text
counts/mm = PulsesPerRevolution * TrackingMultiplier / MillimetersPerRevolution
m/min = abs(deltaCounts / countsPerMm) * 60 / (1000 * sampleSeconds)
```

Quando il tecnico applica la taratura tachimetro, il nuovo `MillimetersPerRevolution` viene scritto immediatamente in `machine_runtime_config.xml`. Al riavvio viene quindi riutilizzato senza ripetere la calibrazione.

Dalla release `3.0.0.4` anche il valore inserito nel campo HMI `Tachometer speed` viene salvato in:

```xml
<RuntimeBindings>
  <EncoderCalibrationTachometerSpeedMetersPerMinute>40</EncoderCalibrationTachometerSpeedMetersPerMinute>
</RuntimeBindings>
```

Il valore `40` resta solo fallback per file macchina vecchi o non ancora aggiornati.

## Avvio automatico

Durante il precaricamento applicazione:

1. viene creato `DigitalIOViewModel`;
2. vengono inizializzate le schede Advantech;
3. viene caricato `machine_runtime_config.xml`;
4. vengono individuati i canali presenti in `EncoderTemplates`;
5. ogni canale configurato viene avviato automaticamente;
6. il contatore principale viene sincronizzato nel `MachineController`;
7. viene registrato `ENCODER_AUTOSTART_READY`.

Il pulsante manuale `Start` resta disponibile esclusivamente per diagnostica e recovery.

## Stabilizzazione velocita all'avvio

La PCIE-1884 puo riportare un conteggio gia avanzato prima che la HMI abbia completato il riferimento interno di velocita. In quel caso il primo delta puo sembrare enorme e saturare la velocita a `250 m/min`.

Dalla release `3.0.0.4`:

1. il primo campione dopo auto-start/re-arm serve solo a sincronizzare il riferimento;
2. nei primi secondi di warmup, un salto non plausibile viene usato come nuova base e non come velocita reale;
3. la velocita pubblicata nel top menu resta a `0` finche non arriva un campione valido successivo.

Questa protezione non cambia il conteggio encoder usato per quote, trigger e MultiShot; stabilizza solo la misura `m/min` visualizzata e pubblicata alla barra superiore.

## Flusso trigger con encoder

Configurazione richiesta:

```xml
<UseEncoderTrigger>true</UseEncoderTrigger>
<TriggerSchedulingMode>VirtualConveyor</TriggerSchedulingMode>
<VirtualConveyorEnabled>false</VirtualConveyorEnabled>
```

Sequenza:

1. la fotocellula rileva il prodotto;
2. la HMI legge il riferimento encoder gia sincronizzato;
3. crea il prodotto con `PRODUCT_ZERO`;
4. per ogni intervention point calcola:

```text
TargetCount = DetectionCount + EffectiveOffsetMm * counts/mm
```

5. nessuna uscita camera viene alzata alla fotocellula;
6. TOP, SIDE/Left e scarto vengono eseguiti solo quando il contatore raggiunge la rispettiva quota;
7. se il riferimento encoder iniziale non e pronto, il fronte fotocellula viene ignorato e loggato invece di generare trigger anticipati.

## Flusso temporizzato

Con:

```xml
<UseEncoderTrigger>false</UseEncoderTrigger>
<TriggerSchedulingMode>TimedFromPhotocell</TriggerSchedulingMode>
```

la fotocellula avvia direttamente i timer:

- `TopTriggerBaseDelayMs`;
- `SideTriggerBaseDelayMs`.

In questa modalita le quote `BaseOffsetMm` degli intervention point non comandano i trigger camera.

## Verifica commissioning

1. Aprire `I/O Diagnostics -> Encoder Setup`.
2. Verificare che il canale principale mostri campionamento attivo senza premere `Start`.
3. Impostare `Usa trigger da encoder reale`.
4. Salvare la configurazione.
5. Controllare che `VirtualConveyorEnabled=false`.
6. Passare un prodotto sulla fotocellula.
7. Verificare nei log prima `PRODUCT_DETECTED`, poi gli eventi punto quota e infine `Pulse START`.
8. Controllare che la distanza reale fotocellula-camera corrisponda a `BaseOffsetMm + TrimOffsetMm`.

Per MultiShot Side/Left, `InitialOffsetPulses` e `StepPulses` restano valori in conteggi encoder grezzi e non vengono convertiti dall'HMI.
