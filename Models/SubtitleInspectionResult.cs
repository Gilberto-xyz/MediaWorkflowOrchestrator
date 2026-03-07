namespace MediaWorkflowOrchestrator.Models
{
    public sealed class SubtitleInspectionResult
    {
        public SubtitleSpanishAvailability Availability { get; set; } = SubtitleSpanishAvailability.Unknown;
        public string Message { get; set; } = string.Empty;
        public IReadOnlyList<string> Matches { get; set; } = Array.Empty<string>();
    }
}
