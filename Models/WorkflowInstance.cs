namespace MediaWorkflowOrchestrator.Models
{
    public sealed class WorkflowInstance
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = "Sin workflow";
        public string RootPath { get; set; } = string.Empty;
        public string SourceRootPath { get; set; } = string.Empty;
        public string PrimaryVideoPath { get; set; } = string.Empty;
        public string SourcePrimaryVideoPath { get; set; } = string.Empty;
        public string TrackCleanupSelectionVideoPath { get; set; } = string.Empty;
        public List<TrackCleanupAudioOption> TrackCleanupAudioOptions { get; set; } = new();
        public List<TrackCleanupSubtitleOption> TrackCleanupSubtitleOptions { get; set; } = new();
        public bool? SourceSelectionIsFile { get; set; }
        public WorkflowStepKey CurrentStep { get; set; } = WorkflowStepKey.InspectSubs;
        public List<WorkflowStepState> Steps { get; set; } = new();
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string LastExecutionSummary { get; set; } = "Sin ejecuciones todavía.";

        public WorkflowStepState? FindStep(WorkflowStepKey key) => Steps.FirstOrDefault(step => step.StepKey == key);
    }
}
