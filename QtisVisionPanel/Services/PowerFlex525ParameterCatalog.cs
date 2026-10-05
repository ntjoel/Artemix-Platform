using QtisVisionPanel.Models;
using System.Collections.Generic;
using System.Linq;

namespace QtisVisionPanel.Services
{
    public static class PowerFlex525ParameterCatalog
    {
        private static readonly List<PowerFlex525ParameterDefinition> Parameters = new List<PowerFlex525ParameterDefinition>
        {
            new PowerFlex525ParameterDefinition { Number = 1, Code = "b001", DisplayName = "Output Frequency", Unit = "Hz", Group = PowerFlex525ParameterGroup.Monitor },
            new PowerFlex525ParameterDefinition { Number = 3, Code = "b003", DisplayName = "Output Current", Unit = "A", Group = PowerFlex525ParameterGroup.Monitor },
            new PowerFlex525ParameterDefinition { Number = 4, Code = "b004", DisplayName = "Output Voltage", Unit = "V", Group = PowerFlex525ParameterGroup.Monitor },
            new PowerFlex525ParameterDefinition { Number = 5, Code = "b005", DisplayName = "DC Bus Voltage", Unit = "V", Group = PowerFlex525ParameterGroup.Monitor },
            new PowerFlex525ParameterDefinition { Number = 6, Code = "b006", DisplayName = "Drive Status", Unit = "", Group = PowerFlex525ParameterGroup.Diagnostics },
            new PowerFlex525ParameterDefinition { Number = 7, Code = "b007", DisplayName = "Fault 1 Code", Unit = "", Group = PowerFlex525ParameterGroup.Diagnostics },
            new PowerFlex525ParameterDefinition { Number = 8, Code = "b008", DisplayName = "Fault 2 Code", Unit = "", Group = PowerFlex525ParameterGroup.Diagnostics },
            new PowerFlex525ParameterDefinition { Number = 9, Code = "b009", DisplayName = "Fault 3 Code", Unit = "", Group = PowerFlex525ParameterGroup.Diagnostics },
            new PowerFlex525ParameterDefinition { Number = 17, Code = "b017", DisplayName = "Output Power", Unit = "kW", Group = PowerFlex525ParameterGroup.Monitor },

            new PowerFlex525ParameterDefinition { Number = 31, Code = "P031", DisplayName = "Motor NP Volts", Unit = "V", IsEditable = true, RequiresStop = true, Group = PowerFlex525ParameterGroup.Motor },
            new PowerFlex525ParameterDefinition { Number = 32, Code = "P032", DisplayName = "Motor NP Hertz", Unit = "Hz", IsEditable = true, RequiresStop = true, Group = PowerFlex525ParameterGroup.Motor },
            new PowerFlex525ParameterDefinition { Number = 33, Code = "P033", DisplayName = "Motor OL Current", Unit = "A", IsEditable = true, Group = PowerFlex525ParameterGroup.Motor },
            new PowerFlex525ParameterDefinition { Number = 34, Code = "P034", DisplayName = "Motor NP FLA", Unit = "A", IsEditable = true, Group = PowerFlex525ParameterGroup.Motor },
            new PowerFlex525ParameterDefinition { Number = 35, Code = "P035", DisplayName = "Motor NP Poles", Unit = "", IsEditable = true, Group = PowerFlex525ParameterGroup.Motor },
            new PowerFlex525ParameterDefinition { Number = 36, Code = "P036", DisplayName = "Motor NP RPM", Unit = "rpm", IsEditable = true, RequiresStop = true, Group = PowerFlex525ParameterGroup.Motor },
            new PowerFlex525ParameterDefinition { Number = 37, Code = "P037", DisplayName = "Motor NP Power", Unit = "kW", IsEditable = true, Group = PowerFlex525ParameterGroup.Motor },

            new PowerFlex525ParameterDefinition { Number = 41, Code = "P041", DisplayName = "Accel Time 1", Unit = "s", IsEditable = true, Group = PowerFlex525ParameterGroup.RampLimit },
            new PowerFlex525ParameterDefinition { Number = 42, Code = "P042", DisplayName = "Decel Time 1", Unit = "s", IsEditable = true, Group = PowerFlex525ParameterGroup.RampLimit },
            new PowerFlex525ParameterDefinition { Number = 43, Code = "P043", DisplayName = "Minimum Freq", Unit = "Hz", IsEditable = true, RequiresStop = true, Group = PowerFlex525ParameterGroup.RampLimit },
            new PowerFlex525ParameterDefinition { Number = 44, Code = "P044", DisplayName = "Maximum Freq", Unit = "Hz", IsEditable = true, RequiresStop = true, Group = PowerFlex525ParameterGroup.RampLimit }
        };

        public static IReadOnlyList<PowerFlex525ParameterDefinition> All => Parameters;

        public static IReadOnlyList<int> TrackedParameterNumbers => Parameters.Select(p => p.Number).ToList();

        public static PowerFlex525ParameterDefinition Find(int number)
        {
            return Parameters.FirstOrDefault(p => p.Number == number);
        }
    }
}
