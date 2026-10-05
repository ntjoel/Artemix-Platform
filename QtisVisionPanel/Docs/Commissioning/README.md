# Commissioning

Questa cartella raccoglie i documenti operativi per:

- test elettrici di base
- commissioning scheda I/O
- commissioning encoder
- validazione quote, trigger e scarto
- raccolta evidenze per la futura integrazione completa nel progetto

Documenti presenti:

- [01_IO_Encoder_Commissioning_Plan.md](./01_IO_Encoder_Commissioning_Plan.md)
- [02_Commissioning_Test_Sheet.md](./02_Commissioning_Test_Sheet.md)
- [03_First_Power_On_Sequence.md](./03_First_Power_On_Sequence.md)
- [04_Bench_Quick_Checklist.md](./04_Bench_Quick_Checklist.md)
- [05_Preconfigured_Signal_Matrix.md](./05_Preconfigured_Signal_Matrix.md)
- [06_PCIE1756_2xADAM3951_Navigator_Test.md](./06_PCIE1756_2xADAM3951_Navigator_Test.md)
- [07_PCIE1884_ADAM3937_First_Encoder_Test.md](./07_PCIE1884_ADAM3937_First_Encoder_Test.md)
- [09_Machine_IO_Setup_Unified_View.md](./09_Machine_IO_Setup_Unified_View.md)
- [10_Job_Editor_Live_Preview_Hardware_Trigger.md](./10_Job_Editor_Live_Preview_Hardware_Trigger.md)
- [11_QtisVision_Offline_Installation.md](./11_QtisVision_Offline_Installation.md)
- [12_VisionPro_Synthetic_MultiCamera_Simulation.md](./12_VisionPro_Synthetic_MultiCamera_Simulation.md)
- [13_VisionPro_ViDi_Classify_Performance.md](./13_VisionPro_ViDi_Classify_Performance.md)
- [14_VisionPro_AI_Classification_Card.md](./14_VisionPro_AI_Classification_Card.md)
- [../../Documentation/MachineHardware/io-encoder-electrical-schematic-current-baseline-2026-06-25.md](../../Documentation/MachineHardware/io-encoder-electrical-schematic-current-baseline-2026-06-25.md)
- [../../Documentation/MachineHardware/io-standalone-bench-validation-alignment-2026-04-22.md](../../Documentation/MachineHardware/io-standalone-bench-validation-alignment-2026-04-22.md)
- [../../Documentation/MachineHardware/io-main-project-alignment-update-2026-04-23.md](../../Documentation/MachineHardware/io-main-project-alignment-update-2026-04-23.md)
- [../../Documentation/MachineHardware/encoder-bench-bringup-alignment-2026-04-23.md](../../Documentation/MachineHardware/encoder-bench-bringup-alignment-2026-04-23.md)

Uso consigliato:

1. leggere il piano prima di andare in macchina
2. stampare o tenere aperto il documento durante i test
3. compilare risultati, anomalie e note direttamente in un report separato o nello stesso file se il workflow di team lo consente
4. congelare la configurazione validata solo a fine commissioning

Ordine consigliato:

1. usare `03_First_Power_On_Sequence.md` alla prima accensione dopo il collegamento dei connettori
2. usare `01_IO_Encoder_Commissioning_Plan.md` per il commissioning completo
3. usare `02_Commissioning_Test_Sheet.md` per registrare ogni verifica ed evidenza
4. tenere `04_Bench_Quick_Checklist.md` aperto durante il collegamento fisico e il primo bring-up
5. usare `05_Preconfigured_Signal_Matrix.md` come base dei segnali gia' predisposti per questa famiglia macchina
6. usare `06_PCIE1756_2xADAM3951_Navigator_Test.md` per il primo test pratico con Navigator su input e output
7. usare `07_PCIE1884_ADAM3937_First_Encoder_Test.md` per il primo bring-up banco dell'encoder su `Counter0 / ENC1`
8. usare `09_Machine_IO_Setup_Unified_View.md` per configurare encoder, tachimetro, punti intervento, uscite fisiche, binding runtime e backup dalla pagina unificata `Machine Setup`
9. usare `10_Job_Editor_Live_Preview_Hardware_Trigger.md` per provare la camera dal Job Tool Editor quando la Live Preview VisionPro standard non scatta con trigger hardware esterno e per regolare l'esposizione in fase di calibrazione
10. usare `11_QtisVision_Offline_Installation.md` per preparare e verificare una postazione installata dal media offline
11. usare `12_VisionPro_Synthetic_MultiCamera_Simulation.md` per collaudare VPP multi-camera con immagini sintetiche senza confondere i tempi indipendenti dei job con un guasto hardware
12. usare `13_VisionPro_ViDi_Classify_Performance.md` per confrontare il tempo del classificatore ViDi EL tra QuickBuild e HMI e verificare affinity, warm-up e debug
13. usare `14_VisionPro_AI_Classification_Card.md` per configurare e collaudare classe e score mostrati nelle viste camera
14. usare `../../Documentation/MachineHardware/io-encoder-electrical-schematic-current-baseline-2026-06-25.md` come schema elettrico applicativo corrente per I/O, encoder, trigger e scarto
15. usare `../../Documentation/MachineHardware/io-standalone-bench-validation-alignment-2026-04-22.md` per sapere cosa e' gia' stato validato nel tool standalone e cosa deve restare allineato nel progetto completo
16. usare `../../Documentation/MachineHardware/io-main-project-alignment-update-2026-04-23.md` per sapere cosa e' gia' stato riportato nel codice e nella documentazione del progetto principale
17. usare `../../Documentation/MachineHardware/encoder-bench-bringup-alignment-2026-04-23.md` per il ramo encoder gia' preparato in entrambi i progetti
