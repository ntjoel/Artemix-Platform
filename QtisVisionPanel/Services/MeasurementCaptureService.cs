using NLog;
using QtisVisionPanel.Database;
using QtisVisionPanel.DataManage;
using QtisVisionPanel.Extensions;
using QtisVisionPanel.Models;
using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Fondazione dati AI (Fase 0) — cattura delle misure strutturate di ispezione.
    ///
    /// Estrae dai <see cref="ValidationResult"/> di un'ispezione i valori numerici
    /// misurati (valore, tolleranza, esito) per ogni feature di ogni camera e li
    /// persiste in modo asincrono e non bloccante tramite
    /// <see cref="InspectionMeasurementRepository"/>.
    ///
    /// Attivazione controllata dal flag di config
    /// <c>MachineRuntimeBindings.DataFoundationCaptureEnabled</c> (default: disabilitato sulle nuove configurazioni).
    /// Il flag viene letto una sola volta alla creazione del servizio: modificarlo
    /// richiede il riavvio dell'applicazione (toggle di deploy, non runtime).
    ///
    /// Non modifica in alcun modo il flusso di ispezione/scarto/contatori: e' additivo
    /// e in caso di errore o DB non disponibile degrada silenziosamente.
    /// </summary>
    public class MeasurementCaptureService
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly InspectionMeasurementRepository _repository = new InspectionMeasurementRepository();
        private volatile bool _captureEnabled;

        public MeasurementCaptureService()
        {
            _captureEnabled = ReadEnabledFlag();
            _logger.Info($"MEASUREMENT_CAPTURE_INIT|enabled={_captureEnabled}");
        }

        public bool CaptureEnabled => _captureEnabled;

        /// <summary>Rilegge il flag di abilitazione dalla configurazione runtime.</summary>
        public void RefreshConfiguration()
        {
            _captureEnabled = ReadEnabledFlag();
        }

        /// <summary>
        /// Cattura (fire-and-forget) tutte le misure di un'ispezione completata.
        /// Chiamata dal punto di finalizzazione ispezione in MainWindow.
        /// </summary>
        public void CaptureInspection(
            InspectionResult result,
            string recipe,
            long productId,
            string topRole,
            string sideRole,
            string frontRole,
            string rearRole,
            string bottomRole,
            string rightRole = null)
        {
            if (!_captureEnabled || result == null)
                return;

            try
            {
                var timestamp = DateTime.Now;
                var entries = new List<InspectionMeasurementEntry>();

                AppendMeasurements(entries, timestamp, productId, recipe, topRole, result.TopResult);
                AppendMeasurements(entries, timestamp, productId, recipe, sideRole, result.SideResult);
                AppendMeasurements(entries, timestamp, productId, recipe, frontRole, result.FrontResult);
                AppendMeasurements(entries, timestamp, productId, recipe, rearRole, result.RearResult);
                AppendMeasurements(entries, timestamp, productId, recipe, rightRole, result.RightResult);
                AppendMeasurements(entries, timestamp, productId, recipe, bottomRole, result.BottomResult);

                if (entries.Count == 0)
                    return;

                // Fase 1 (SPC): alimenta il controllo statistico di processo in-memory
                // prima della scrittura DB. Sincrono e veloce; non rilegge il database.
                ServiceLocator.ProcessControlService?.ObserveBatch(entries);

                _repository.InsertBatchAsync(entries)
                    .SafeFireAndForget(_logger, "MEASUREMENT_CAPTURE_WRITE_FAILED");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "MEASUREMENT_CAPTURE_FAILED");
            }
        }

        private static void AppendMeasurements(
            List<InspectionMeasurementEntry> entries,
            DateTime timestamp,
            long productId,
            string recipe,
            string cameraRole,
            ValidationResult validation)
        {
            if (validation?.Measurements == null || string.IsNullOrWhiteSpace(cameraRole))
                return;

            string role = CameraConfigurationHelper.NormalizeCameraType(cameraRole);

            foreach (var kv in validation.Measurements)
            {
                entries.Add(new InspectionMeasurementEntry
                {
                    Timestamp = timestamp,
                    ProductId = productId,
                    Recipe = recipe,
                    CameraRole = role,
                    Feature = kv.Key,
                    MeasuredValue = kv.Value.Value,
                    Tolerance = kv.Value.Tolerance,
                    IsDefect = !kv.Value.Passed
                });
            }
        }

        private static bool ReadEnabledFlag()
        {
            try
            {
                var config = new MachineConfigurationService().Load();
                return config?.RuntimeBindings?.DataFoundationCaptureEnabled ?? false;
            }
            catch
            {
                return false;
            }
        }
    }
}
