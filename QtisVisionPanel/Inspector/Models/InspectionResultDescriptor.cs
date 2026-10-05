namespace QtisVisionPanel.Inspector.Models
{
    public sealed class InspectionResultDescriptor
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Threshold { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }
}
