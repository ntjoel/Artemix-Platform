using Cognex.Vision;
using NLog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace QtisVisionPanel.Services
{
    /// <summary>
    /// Initializes the Cognex runtime once, before any VisionPro object is used.
    /// ViDi EL requires the VProX product and security initialization to be ready
    /// before tool templates or persisted RBBT instances are deserialized.
    /// </summary>
    public static class VisionProRuntimeBootstrap
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly object InitializationLock = new object();
        private static readonly object EditorExtensionsLock = new object();
        private static int _isInitialized;
        private static int _editorExtensionsLoaded;

        private static readonly string[] EditorExtensionAssemblies =
        {
            "Cognex.Vision.ViDiELClassify.Net",
            "Cognex.VisionPro.ViDiEL.Controls",
            "Cognex.VisionProUI.ViDiELClassify.Controls"
        };

        public static bool IsInitialized => Volatile.Read(ref _isInitialized) == 1;

        public static void EnsureInitialized()
        {
            if (IsInitialized)
            {
                return;
            }

            lock (InitializationLock)
            {
                if (IsInitialized)
                {
                    return;
                }

                try
                {
                    ValidateStartupAssemblyLocation();

                    // This is the sequence used by the official VisionPro
                    // ViDi EL Classify sample application.
                    Startup.Initialize(Startup.ProductKey.VProX, null, true);

                    if (!Startup.WaitForInitDone(StartupWaitType.Timeout, 30000))
                    {
                        throw new TimeoutException(
                            "VisionPro security initialization did not complete within 30 seconds.");
                    }

                    Volatile.Write(ref _isInitialized, 1);
                    Logger.Info(
                        "VISIONPRO_RUNTIME_INITIALIZED|product=VProX; security=True; {0}",
                        DescribeRuntimeLocations());
                }
                catch (Exception ex)
                {
                    Logger.Error(
                        "VISIONPRO_RUNTIME_INITIALIZATION_FAILED|{0}",
                        DescribeException(ex));
                    throw new System.InvalidOperationException(
                        "Impossibile inizializzare il runtime Cognex VisionPro VProX.",
                        ex);
                }
            }
        }

        /// <summary>
        /// Loads optional editor plug-ins before CogToolBlockEditV2 builds its
        /// palette. A failure is logged but does not block classic VisionPro tools.
        /// </summary>
        public static bool TryPrepareJobEditor(out string failureDetails)
        {
            failureDetails = null;

            try
            {
                EnsureInitialized();

                if (Volatile.Read(ref _editorExtensionsLoaded) == 1)
                {
                    return true;
                }

                lock (EditorExtensionsLock)
                {
                    if (Volatile.Read(ref _editorExtensionsLoaded) == 1)
                    {
                        return true;
                    }

                    string runtimeDirectory = ResolveVisionProBinDirectory();
                    var loaded = new StringBuilder();

                    foreach (string assemblyName in EditorExtensionAssemblies)
                    {
                        Assembly assembly = LoadEditorExtension(assemblyName, runtimeDirectory);
                        if (loaded.Length > 0)
                        {
                            loaded.Append(',');
                        }

                        loaded.Append(assembly.GetName().Name)
                            .Append('@')
                            .Append(assembly.GetName().Version);
                    }

                    Volatile.Write(ref _editorExtensionsLoaded, 1);
                    Logger.Info("VISIONPRO_JOB_EDITOR_EXTENSIONS_READY|assemblies={0}", loaded);
                    return true;
                }
            }
            catch (Exception ex)
            {
                failureDetails = DescribeException(ex);
                Logger.Warn("VISIONPRO_JOB_EDITOR_EXTENSIONS_FAILED|{0}", failureDetails);
                return false;
            }
        }

        public static string DescribeException(Exception exception)
        {
            if (exception == null)
            {
                return "exception=null";
            }

            var details = new StringBuilder();
            Exception current = exception;

            for (int depth = 0; current != null && depth < 16; depth++)
            {
                if (details.Length > 0)
                {
                    details.Append(" --> ");
                }

                details.Append('[')
                    .Append(depth)
                    .Append("] ")
                    .Append(current.GetType().FullName)
                    .Append(" (0x")
                    .Append(current.HResult.ToString("X8"))
                    .Append("): ")
                    .Append(current.Message);

                if (current is ReflectionTypeLoadException typeLoadException &&
                    typeLoadException.LoaderExceptions != null)
                {
                    foreach (Exception loaderException in typeLoadException.LoaderExceptions)
                    {
                        if (loaderException == null)
                        {
                            continue;
                        }

                        details.Append(" | Loader: ")
                            .Append(loaderException.GetType().FullName)
                            .Append(": ")
                            .Append(loaderException.Message);
                    }
                }

                current = current.InnerException;
            }

            return details.ToString();
        }

        public static string DescribeRuntimeLocations()
        {
            string startupAssembly = SafeAssemblyLocation(typeof(Startup).Assembly);
            string runtimeDirectory = ResolveVisionProBinDirectory() ?? "unresolved";
            string applicationDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string localStartup = Path.Combine(applicationDirectory, "Cognex.Vision.Startup.Net.dll");

            string nativeModules;
            try
            {
                nativeModules = string.Join(
                    ",",
                    Process.GetCurrentProcess().Modules
                        .Cast<ProcessModule>()
                        .Where(module =>
                            module.ModuleName.IndexOf("onnx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            module.ModuleName.IndexOf("rbbt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            module.ModuleName.IndexOf("vidi", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(module => module.ModuleName + "@" + module.FileName));
            }
            catch (Exception ex)
            {
                nativeModules = "unavailable:" + ex.Message;
            }

            return string.Format(
                "startupAssembly={0}; runtime={1}; localStartupPresent={2}; nativeModules={3}",
                startupAssembly,
                runtimeDirectory,
                File.Exists(localStartup),
                string.IsNullOrWhiteSpace(nativeModules) ? "none" : nativeModules);
        }

        private static void ValidateStartupAssemblyLocation()
        {
            string applicationDirectory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string startupAssembly = SafeAssemblyLocation(typeof(Startup).Assembly);
            string startupDirectory = string.IsNullOrWhiteSpace(startupAssembly)
                ? string.Empty
                : Path.GetDirectoryName(Path.GetFullPath(startupAssembly))
                    ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(
                applicationDirectory,
                startupDirectory,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new System.InvalidOperationException(
                    "Cognex.Vision.Startup.Net.dll non deve trovarsi accanto a QtisVisionPanel.exe. " +
                    "Deve essere risolta tramite VisionProDependencies insieme a RBBT/ViDi EL. " +
                    "Rimuovere la copia locale e ricreare la junction VisionProDependencies.");
            }
        }

        private static string SafeAssemblyLocation(Assembly assembly)
        {
            try
            {
                return assembly?.Location ?? "unresolved";
            }
            catch
            {
                return "unresolved";
            }
        }

        private static Assembly LoadEditorExtension(string assemblyName, string runtimeDirectory)
        {
            try
            {
                return Assembly.Load(new AssemblyName(assemblyName));
            }
            catch (FileNotFoundException) when (!string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                string assemblyPath = Path.Combine(runtimeDirectory, assemblyName + ".dll");
                if (!File.Exists(assemblyPath))
                {
                    throw new FileNotFoundException(
                        $"VisionPro editor extension not found: {assemblyName}",
                        assemblyPath);
                }

                return Assembly.LoadFrom(assemblyPath);
            }
        }

        private static string ResolveVisionProBinDirectory()
        {
            string localDependencies = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "VisionProDependencies");

            if (ContainsVisionProSentinel(localDependencies))
            {
                return localDependencies;
            }

            string visionProRoot = Environment.GetEnvironmentVariable("VPRO_ROOT");
            if (!string.IsNullOrWhiteSpace(visionProRoot))
            {
                string configuredBin = Path.Combine(visionProRoot, "bin");
                if (ContainsVisionProSentinel(configuredBin))
                {
                    return configuredBin;
                }
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string standardBin = Path.Combine(programFiles, "Cognex", "VisionPro", "bin");
            return ContainsVisionProSentinel(standardBin) ? standardBin : null;
        }

        private static bool ContainsVisionProSentinel(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory) &&
                   File.Exists(Path.Combine(directory, "Cognex.Vision.Startup.Net.dll"));
        }
    }
}
