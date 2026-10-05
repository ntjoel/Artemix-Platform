using System.Collections.Generic;
using System.Xml.Serialization;

namespace QtisVisionPanel.Cls_Config.Calss_structure
{
    [XmlRoot("OpcUaConfig")]
    public class OpcUaConfig
    {
        public bool Enabled { get; set; } = false;
        public bool RequiredForMachineRun { get; set; } = false;
        public bool UseDatabaseConfiguration { get; set; } = true;
        public string ServerUrl { get; set; } = "opc.tcp://127.0.0.1:4840";
        public string ApplicationName { get; set; } = "QtisVisionPanel";
        public bool UseSecurity { get; set; } = false;
        public bool AutoAcceptUntrustedCertificates { get; set; } = false;
        public bool RemoteCommandsEnabled { get; set; } = true;
        public bool RemoteStartStopEnabled { get; set; } = true;
        public bool RemoteRecipeChangeEnabled { get; set; } = true;
        public bool AuditRemoteCommandsToDatabase { get; set; } = true;
        public int ReconnectIntervalMs { get; set; } = 5000;
        public int SubscriptionIntervalMs { get; set; } = 250;
        public int SessionTimeoutMs { get; set; } = 60000;
        public int OperationTimeoutMs { get; set; } = 5000;

        [XmlArray("Nodes")]
        [XmlArrayItem("Node")]
        public List<OpcUaNodeConfig> Nodes { get; set; } = CreateDefaultNodes();

        public static List<OpcUaNodeConfig> CreateDefaultNodes()
        {
            return new List<OpcUaNodeConfig>
            {
                new OpcUaNodeConfig("Start", "ns=2;s=QtisVision.Commands.Start", "ServerToClient", "Boolean", true, false, "PLC/HMI request to start continuous run."),
                new OpcUaNodeConfig("Stop", "ns=2;s=QtisVision.Commands.Stop", "ServerToClient", "Boolean", true, false, "PLC/HMI request to stop continuous run."),
                new OpcUaNodeConfig("RecipeName", "ns=2;s=QtisVision.Commands.RecipeName", "ServerToClient", "String", true, false, "Recipe name requested by the external system."),
                new OpcUaNodeConfig("RecipeId", "ns=2;s=QtisVision.Commands.RecipeId", "ServerToClient", "Int64", true, false, "Database IdProduzione requested by the external system."),
                new OpcUaNodeConfig("CommandId", "ns=2;s=QtisVision.Commands.CommandId", "ServerToClient", "Int64", true, false, "Monotonic command sequence written last by the server."),
                new OpcUaNodeConfig("DataAckSequence", "ns=2;s=QtisVision.Handshake.DataAckSequence", "ServerToClient", "Int64", true, false, "Server acknowledgement for the last consumed HMI data sequence."),
                new OpcUaNodeConfig("DataAckOk", "ns=2;s=QtisVision.Handshake.DataAckOk", "ServerToClient", "Boolean", true, false, "Server result for the acknowledged HMI data sequence."),
                new OpcUaNodeConfig("DataAckMessage", "ns=2;s=QtisVision.Handshake.DataAckMessage", "ServerToClient", "String", true, false, "Server acknowledgement message for HMI data exchange."),
                new OpcUaNodeConfig("TotalCount", "ns=2;s=QtisVision.Status.TotalCount", "ClientToServer", "Int64", true, false, "Total inspected products."),
                new OpcUaNodeConfig("GoodCount", "ns=2;s=QtisVision.Status.GoodCount", "ClientToServer", "Int64", true, false, "Good products."),
                new OpcUaNodeConfig("BadCount", "ns=2;s=QtisVision.Status.BadCount", "ClientToServer", "Int64", true, false, "Rejected or non-compliant products."),
                new OpcUaNodeConfig("LastResult", "ns=2;s=QtisVision.Status.LastResult", "ClientToServer", "Boolean", true, false, "True when last inspection was good."),
                new OpcUaNodeConfig("CurrentRecipe", "ns=2;s=QtisVision.Status.CurrentRecipe", "ClientToServer", "String", true, false, "Recipe currently loaded in production."),
                new OpcUaNodeConfig("CurrentRecipeId", "ns=2;s=QtisVision.Status.CurrentRecipeId", "ClientToServer", "Int64", true, false, "Database IdProduzione of the recipe currently loaded in production."),
                new OpcUaNodeConfig("IsRunning", "ns=2;s=QtisVision.Status.IsRunning", "ClientToServer", "Boolean", true, false, "Vision continuous run state."),
                new OpcUaNodeConfig("Heartbeat", "ns=2;s=QtisVision.Status.Heartbeat", "ClientToServer", "Int64", true, false, "Incremental communication heartbeat."),
                new OpcUaNodeConfig("CommandAckId", "ns=2;s=QtisVision.Handshake.CommandAckId", "ClientToServer", "Int64", true, false, "Last command sequence processed by the HMI."),
                new OpcUaNodeConfig("CommandAckOk", "ns=2;s=QtisVision.Handshake.CommandAckOk", "ClientToServer", "Boolean", true, false, "True when the HMI command was processed successfully."),
                new OpcUaNodeConfig("CommandAckMessage", "ns=2;s=QtisVision.Handshake.CommandAckMessage", "ClientToServer", "String", true, false, "HMI command acknowledgement message."),
                new OpcUaNodeConfig("CommandAckTimestamp", "ns=2;s=QtisVision.Handshake.CommandAckTimestamp", "ClientToServer", "String", true, false, "HMI command acknowledgement timestamp in ISO-8601 format."),
                new OpcUaNodeConfig("DataSequence", "ns=2;s=QtisVision.Handshake.DataSequence", "ClientToServer", "Int64", true, false, "Monotonic HMI data snapshot sequence written after status values."),
                new OpcUaNodeConfig("DataPublishOk", "ns=2;s=QtisVision.Handshake.DataPublishOk", "ClientToServer", "Boolean", true, false, "True when the HMI status snapshot was written locally without OPC write errors."),
                new OpcUaNodeConfig("DataPublishMessage", "ns=2;s=QtisVision.Handshake.DataPublishMessage", "ClientToServer", "String", true, false, "HMI data publication message."),
                new OpcUaNodeConfig("DataPublishTimestamp", "ns=2;s=QtisVision.Handshake.DataPublishTimestamp", "ClientToServer", "String", true, false, "HMI data publication timestamp in ISO-8601 format."),
                // AI Fase 8 — preallarmi salute macchina verso MES/SCADA (deriva ispezioni + salute PC).
                // Read-only ClientToServer: pubblicati solo se il nodo e' mappato/abilitato (altrimenti no-op).
                new OpcUaNodeConfig("EarlyWarningActive", "ns=2;s=QtisVision.Health.EarlyWarningActive", "ClientToServer", "Boolean", true, false, "True quando e' attivo almeno un preallarme salute macchina."),
                new OpcUaNodeConfig("EarlyWarningSeverity", "ns=2;s=QtisVision.Health.EarlyWarningSeverity", "ClientToServer", "String", true, false, "Severita' massima attiva: None|Info|Warning|Critical."),
                new OpcUaNodeConfig("EarlyWarningCode", "ns=2;s=QtisVision.Health.EarlyWarningCode", "ClientToServer", "String", true, false, "Categoria del preallarme guida: Inspection|PcHealth."),
                new OpcUaNodeConfig("EarlyWarningMessage", "ns=2;s=QtisVision.Health.EarlyWarningMessage", "ClientToServer", "String", true, false, "Messaggio del preallarme guida / riepilogo digest."),
                new OpcUaNodeConfig("EarlyWarningEtaValue", "ns=2;s=QtisVision.Health.EarlyWarningEtaValue", "ClientToServer", "Double", true, false, "Stima 'quanto manca al limite' del preallarme guida."),
                new OpcUaNodeConfig("EarlyWarningEtaUnit", "ns=2;s=QtisVision.Health.EarlyWarningEtaUnit", "ClientToServer", "String", true, false, "Unita' della stima: pezzi|ore."),
                new OpcUaNodeConfig("EarlyWarningCount", "ns=2;s=QtisVision.Health.EarlyWarningCount", "ClientToServer", "Int64", true, false, "Numero di preallarmi nel lotto/digest corrente."),
                new OpcUaNodeConfig("EarlyWarningTimestamp", "ns=2;s=QtisVision.Health.EarlyWarningTimestamp", "ClientToServer", "String", true, false, "Timestamp ISO-8601 dell'ultimo preallarme pubblicato."),
                new OpcUaNodeConfig("EarlyWarningSequence", "ns=2;s=QtisVision.Health.EarlyWarningSequence", "ClientToServer", "Int64", true, false, "Sequenza monotona: il MES rileva un nuovo preallarme anche se il testo si ripete.")
            };
        }
    }

    public class OpcUaNodeConfig
    {
        public OpcUaNodeConfig()
        {
        }

        public OpcUaNodeConfig(string key, string nodeId, string direction, string dataType, bool enabled, bool required, string description)
        {
            Key = key;
            NodeId = nodeId;
            Direction = direction;
            DataType = dataType;
            Enabled = enabled;
            Required = required;
            Description = description;
        }

        [XmlAttribute]
        public string Key { get; set; }

        [XmlAttribute]
        public string NodeId { get; set; }

        [XmlAttribute]
        public string Direction { get; set; }

        [XmlAttribute]
        public string DataType { get; set; }

        [XmlAttribute]
        public bool Enabled { get; set; } = true;

        [XmlAttribute]
        public bool Required { get; set; }

        [XmlAttribute]
        public string Description { get; set; }
    }
}
