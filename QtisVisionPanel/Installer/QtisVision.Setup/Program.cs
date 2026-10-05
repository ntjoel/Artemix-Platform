using System.Text.Json;
using Microsoft.Win32;

namespace QtisVision.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string manifestPath = Path.Combine(AppContext.BaseDirectory, "installer-manifest.json");
        if (args.Any(arg => arg.Equals("--validate", StringComparison.OrdinalIgnoreCase)))
        {
            return RunValidationAsync(manifestPath).GetAwaiter().GetResult();
        }

        if (args.Any(arg => arg.Equals("--inventory", StringComparison.OrdinalIgnoreCase)))
        {
            return RunInventory(manifestPath);
        }

        if (!File.Exists(manifestPath))
        {
            MessageBox.Show(
                $"Manifest installer non trovato:\n{manifestPath}\n\nMantenere QtisVisionSetup.exe insieme alla cartella Payloads.",
                "Qtis Vision Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        try
        {
            Application.Run(new MainForm(manifestPath));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Avvio installer fallito:\n{ex.Message}",
                "Qtis Vision Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static async Task<int> RunValidationAsync(string manifestPath)
    {
        string reportPath = Path.Combine(
            Path.GetDirectoryName(manifestPath) ?? AppContext.BaseDirectory,
            "installer-validation.json");

        try
        {
            using InstallerEngine engine = new(manifestPath);
            ValidationReport report = await engine.ValidateAsync(
                engine.Manifest.Components.Where(component => component.Enabled).Select(component => component.Id),
                CancellationToken.None);

            string json = JsonSerializer.Serialize(report, InstallerEngine.JsonOptions);
            File.WriteAllText(reportPath, json);
            Console.WriteLine(json);
            return report.IsValid ? 0 : 3;
        }
        catch (Exception ex)
        {
            string json = JsonSerializer.Serialize(new
            {
                valid = false,
                error = ex.ToString()
            }, InstallerEngine.JsonOptions);
            File.WriteAllText(reportPath, json);
            Console.Error.WriteLine(json);
            return 1;
        }
    }

    private static int RunInventory(string manifestPath)
    {
        string reportPath = Path.Combine(
            Path.GetDirectoryName(manifestPath) ?? AppContext.BaseDirectory,
            "installer-inventory.json");

        try
        {
            using InstallerEngine engine = new(manifestPath);
            CoreInstallationInfo coreInfo = engine.GetCoreInstallationInfo();
            var report = new
            {
                productVersion = engine.Manifest.ProductVersion,
                mediaRevision = engine.Manifest.MediaRevision,
                generatedAtUtc = DateTime.UtcNow,
                hardware = ReadHardwareInventory(),
                core = new
                {
                    targetVersion = coreInfo.TargetVersion,
                    installedVersion = coreInfo.InstalledVersion,
                    mode = coreInfo.Mode,
                    coreInfo.IsDowngradeBlocked,
                    summary = coreInfo.DisplayText
                },
                components = engine.Manifest.Components.Select(component => new
                {
                    component.Id,
                    component.DisplayName,
                    status = engine.Detect(component).ToString(),
                    component.MinimumVersion
                })
            };

            string json = JsonSerializer.Serialize(report, InstallerEngine.JsonOptions);
            File.WriteAllText(reportPath, json);
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex)
        {
            string json = JsonSerializer.Serialize(new
            {
                valid = false,
                error = ex.ToString()
            }, InstallerEngine.JsonOptions);
            File.WriteAllText(reportPath, json);
            Console.Error.WriteLine(json);
            return 1;
        }
    }

    private static HardwareInventory ReadHardwareInventory()
    {
        string manufacturer = ReadRegistryText(
            @"HARDWARE\DESCRIPTION\System\BIOS",
            "SystemManufacturer");
        string model = ReadRegistryText(
            @"HARDWARE\DESCRIPTION\System\BIOS",
            "SystemProductName");
        string baseBoardManufacturer = ReadRegistryText(
            @"HARDWARE\DESCRIPTION\System\BIOS",
            "BaseBoardManufacturer");
        string baseBoardProduct = ReadRegistryText(
            @"HARDWARE\DESCRIPTION\System\BIOS",
            "BaseBoardProduct");
        string biosVersion = ReadRegistryText(
            @"HARDWARE\DESCRIPTION\System\BIOS",
            "BIOSVersion");
        string processor = ReadRegistryText(
            @"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString");
        bool generic = IsGenericHardwareText(manufacturer) || IsGenericHardwareText(model);

        return new HardwareInventory
        {
            Manufacturer = manufacturer,
            Model = model,
            Description = string.Join(
                " ",
                new[] { manufacturer, model }.Where(value => !string.IsNullOrWhiteSpace(value))),
            BaseBoardManufacturer = baseBoardManufacturer,
            BaseBoardProduct = baseBoardProduct,
            BiosVersion = biosVersion,
            ProcessorName = processor,
            ModelIsGeneric = generic
        };
    }

    private static string ReadRegistryText(string subKey, string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            return Convert.ToString(key?.GetValue(valueName))?.Trim() ?? string.Empty;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return string.Empty;
        }
    }

    private static bool IsGenericHardwareText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return new[]
        {
            "default string",
            "system product name",
            "to be filled by o.e.m.",
            "unknown"
        }.Any(generic => value.Equals(generic, StringComparison.OrdinalIgnoreCase));
    }
}
