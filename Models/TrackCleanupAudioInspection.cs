namespace MediaWorkflowOrchestrator.Models
{
    public sealed class TrackCleanupAudioInspection
    {
        public bool CanManuallySelectAudio { get; set; }
        public string Message { get; set; } = string.Empty;
        public string TargetVideoPath { get; set; } = string.Empty;
        public string SpecialCasesMessage { get; set; } = string.Empty;
        public List<TrackCleanupAudioOption> AudioOptions { get; set; } = new();
        public List<TrackCleanupSubtitleOption> SubtitleOptions { get; set; } = new();
        public List<TrackCleanupSpecialCaseItem> SpecialCases { get; set; } = new();
    }
}
