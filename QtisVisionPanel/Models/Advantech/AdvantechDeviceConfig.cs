using System;
using System.Collections.Generic;

namespace QtisVisionPanel.Models.Advantech
{
    public class AdvantechDeviceConfig
    {
        public string DeviceName { get; set; }
        public string DeviceDescription { get; set; }
        public int DeviceNumber { get; set; }
        public DeviceType Type { get; set; }
        public Dictionary<string, string> Parameters { get; set; }

        public enum DeviceType
        {
            PCIE1756,
            PCIE1884
        }
    }

    public class IOMappingConfig
    {
        public Dictionary<int, IOLogic> InputActions { get; set; }
        public Dictionary<int, OutputSequence> OutputSequences { get; set; }
        public List<CounterTrigger> CounterTriggers { get; set; }
    }

    public class IOLogic
    {
        public int InputChannel { get; set; }
        public List<Action> Actions { get; set; }
    }

    public class Action
    {
        public ActionType Type { get; set; }
        public int TargetChannel { get; set; }
        public bool Value { get; set; }
        public int DelayMs { get; set; }
        public ActionCondition Condition { get; set; }
    }

    public enum ActionType
    {
        SetOutput,
        PulseOutput,
        ToggleOutput,
        ResetCounter,
        StartCounter,
        StopCounter
    }

    public class ActionCondition
    {
        public ConditionType Type { get; set; }
        public int Value { get; set; }
        public int DurationMs { get; set; }
    }

    public enum ConditionType
    {
        None,
        RisingEdge,
        FallingEdge,
        Duration,
        CounterValue
    }

    public class OutputSequence
    {
        public int[] Channels { get; set; }
        public bool[] States { get; set; }
        public int DurationMs { get; set; }
        public bool Loop { get; set; }
    }

    public class CounterTrigger
    {
        public int CounterChannel { get; set; }
        public int TriggerValue { get; set; }
        public Action[] Actions { get; set; }
    }
}