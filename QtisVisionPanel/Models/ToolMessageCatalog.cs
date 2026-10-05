using System.Collections.Generic;
using System.Runtime.Serialization;

namespace QtisVisionPanel.Models
{
    [DataContract]
    public class ToolMessageCatalog
    {
        [DataMember]
        public string key { get; set; } = "ENG";

        [DataMember]
        public Dictionary<string, string> messages { get; set; } = new Dictionary<string, string>();
    }
}
