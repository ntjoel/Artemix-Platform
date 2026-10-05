# Pubblicazione GitHub del 2026-10-05

Questa copia pubblica deriva dai sorgenti della release 3.1.6.1 e dal commit operativo `786ed5febf3cf6716260d618001436a6f8011f21`.

Lo storico locale contiene output di build e configurazioni personali; la pubblicazione parte dal commit iniziale remoto e aggiunge la baseline sorgente corrente.

## Credenziali da configurare

- `QtisVisionPanel/Cls_Config/Calss_structure/ConfigClassStructure.cs`: la password MySQL predefinita e' `CHANGE_ME`. Configurare le credenziali effettive tramite il contratto `Config.xml` del progetto prima dell'avvio.
- `QtisVisionPanel/Installer/QtisVision.Setup/InstallerEngine.cs`: la password UltraVNC incorporata e' sostituita dalla variabile d'ambiente `QTIS_ULTRAVNC_PASSWORD`. Configurarla prima di eseguire il provisioning dell'assistenza remota; in assenza del valore il provisioning segnala un errore esplicito.

Queste sostituzioni riguardano esclusivamente la copia pubblicata. La copia operativa locale non viene modificata.

## File distribuiti

Sorgenti, risorse, progetti, script, template e documentazione sono inclusi. I pacchetti NuGet devono essere ripristinati. Le dipendenze vendor richiedono installazione e licenze separate.

Output `bin`/`obj`, cache IDE, impostazioni personali degli assistenti, backup, log e file di credenziali non sono inclusi.
