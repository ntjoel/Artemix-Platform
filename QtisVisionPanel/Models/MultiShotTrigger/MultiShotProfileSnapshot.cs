using System.Collections.Generic;
using QtisVisionPanel.Models.MultiShotTrigger;

namespace QtisVisionPanel.Models.MultiShotTrigger
{
    /// <summary>
    /// Snapshot dei parametri MultiShot dell'ultimo salvataggio per un profilo.
    /// Usato in DigitalIOViewModel per mostrare "Prec.: X" sotto ogni campo editor.
    /// Sostituito come oggetto intero — non implementa INPC.
    /// </summary>
    public sealed class MultiShotProfileSnapshot
    {
        public static readonly MultiShotProfileSnapshot Empty = new MultiShotProfileSnapshot();

        public bool HasValues { get; set; }
        public string MaxShots { get; set; } = "";
        public string Timeout { get; set; } = "";
        public string TriggerOutput { get; set; } = "";
        public string ShotCount { get; set; } = "";
        public string PulseMs { get; set; } = "";
        public string MinIntervalMs { get; set; } = "";
        public string InitialOffsetPulses { get; set; } = "";
        public string StepPulses { get; set; } = "";
        public string StepMm { get; set; } = "";
        public string MmPerPixel { get; set; } = "";

        public static MultiShotProfileSnapshot FromOptions(MultiShotTriggerOptions opts)
        {
            if (opts == null) return Empty;
            return new MultiShotProfileSnapshot
            {
                HasValues = true,
                MaxShots = opts.MaxShotCount.ToString(),
                Timeout = opts.SessionTimeoutMs.ToString(),
                TriggerOutput = opts.TriggerOutputName ?? "",
                ShotCount = opts.ShotCount.ToString(),
                PulseMs = opts.TriggerPulseMs.ToString(),
                MinIntervalMs = opts.MinimumInterShotIntervalMs.ToString(),
                InitialOffsetPulses = opts.InitialOffsetPulses.ToString(),
                StepPulses = opts.StepPulses.ToString(),
                StepMm = (opts.VisionProStitching?.StepMm ?? 0.0).ToString("G4"),
                MmPerPixel = (opts.VisionProStitching?.MmPerPixel ?? 0.0).ToString("G4"),
            };
        }

        public static MultiShotProfileSnapshot FromDictionary(Dictionary<string, string> d)
        {
            if (d == null || d.Count == 0) return Empty;
            string Get(string key) => d.TryGetValue(key, out var v) ? v : "";
            return new MultiShotProfileSnapshot
            {
                HasValues = true,
                MaxShots = Get("MaxShotCount"),
                Timeout = Get("SessionTimeoutMs"),
                TriggerOutput = Get("TriggerOutputName"),
                ShotCount = Get("ShotCount"),
                PulseMs = Get("TriggerPulseMs"),
                MinIntervalMs = Get("MinimumInterShotIntervalMs"),
                InitialOffsetPulses = Get("InitialOffsetPulses"),
                StepPulses = Get("StepPulses"),
                StepMm = Get("VisionProStepMm"),
                MmPerPixel = Get("VisionProMmPerPixel"),
            };
        }

        public Dictionary<string, string> ToDictionary()
        {
            return new Dictionary<string, string>
            {
                ["MaxShotCount"]              = MaxShots,
                ["SessionTimeoutMs"]          = Timeout,
                ["TriggerOutputName"]         = TriggerOutput,
                ["ShotCount"]                 = ShotCount,
                ["TriggerPulseMs"]            = PulseMs,
                ["MinimumInterShotIntervalMs"] = MinIntervalMs,
                ["InitialOffsetPulses"]       = InitialOffsetPulses,
                ["StepPulses"]                = StepPulses,
                ["VisionProStepMm"]           = StepMm,
                ["VisionProMmPerPixel"]       = MmPerPixel,
            };
        }
    }
}
