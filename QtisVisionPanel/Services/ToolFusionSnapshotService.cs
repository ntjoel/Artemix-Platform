using QtisVisionPanel.Models;
using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace QtisVisionPanel.Services
{
    public class ToolFusionSnapshotService
    {
        private readonly MachineConfigurationService _machineConfigurationService;

        public ToolFusionSnapshotService(MachineConfigurationService machineConfigurationService)
        {
            _machineConfigurationService = machineConfigurationService ?? throw new ArgumentNullException(nameof(machineConfigurationService));
        }

        public string GetExportFolderPath()
        {
            var exportPath = Path.Combine(_machineConfigurationService.GetConfigurationRootPath(), "fusion-exports");
            Directory.CreateDirectory(exportPath);
            return exportPath;
        }

        public string ExportSnapshot(ToolFusionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Directory.CreateDirectory(GetExportFolderPath());
            snapshot.CreatedUtc = DateTime.UtcNow;

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var filePath = Path.Combine(GetExportFolderPath(), $"tool-fusion-snapshot-{timestamp}.json");

            var serializer = new DataContractJsonSerializer(
                typeof(ToolFusionSnapshot),
                new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

            using (var stream = File.Create(filePath))
            {
                serializer.WriteObject(stream, snapshot);
            }

            return filePath;
        }
    }
}
