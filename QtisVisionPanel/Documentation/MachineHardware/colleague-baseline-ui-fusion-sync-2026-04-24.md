# Colleague Baseline UI Fusion Sync - 2026-04-24

## Scope

Questo documento registra il completamento del riallineamento del repo locale full alla baseline UI/tool gia' promossa nel repo del collega.

Repo locale:

- `C:\Users\chouikha\QtisVisionPanel`

Repo baseline di riferimento:

- `S:\Sorgenti e software\VISIONQAI\QtisVisionPanel`

## Allineamento completato

Portati dal repo collega al repo locale:

### UI e viewmodel commissioning

- `ViewModels\DigitalIOViewModel.cs`
- `Views\UserControls\DigitalIOControl.xaml`
- `Views\UserControls\DigitalIOControl.xaml.cs`

### Asset runtime del tool

- `ConfigurationTemplates\machine_runtime_config.template.xml`
- `Localization\messages_eng.json`
- `Localization\messages_ita.json`

## Stato progetto

Il repo locale full era gia' stato riallineato in step precedenti su:

- hardening del post-build VisionPro
- layer I/O comune
- fondazione runtime/configurazione del tool

Con questo step entra anche il primo slice UI completo del commissioning tool, uguale alla baseline collega.

## Verifica build

Comando usato:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'C:\Users\chouikha\QtisVisionPanel\QtisVisionPanel.sln' `
  /t:Build /p:Configuration=Debug /p:Platform=x64 /m
```

Esito:

- `Build succeeded`
- `0 errori`

Nota operativa utile:

- sul repo locale il `PostBuildEvent` ha creato correttamente la giunzione `VisionProDependencies`

## Risultato

Repo collega e repo locale full sono ora allineati sulla stessa baseline per:

- build `Debug|x64`
- layer I/O comune
- fondazione runtime/configurazione del tool
- prima UI completa del commissioning

## Prossimo uso previsto

Da questo punto il lavoro puo' continuare sul progetto completo con maggiore sicurezza per:

- apertura del pannello completo sul PC macchina
- verifica runtime con VisionPro installato
- test di passaggio prodotto reali
- commissioning macchina con I/O ed encoder
