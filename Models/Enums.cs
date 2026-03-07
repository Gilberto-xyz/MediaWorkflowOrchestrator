namespace MediaWorkflowOrchestrator.Models
{
    public enum WorkflowStepKey
    {
        Download,
        InspectSubs,
        TranslateSubs,
        CleanTracks,
        TagAndRename,
        PackageRar,
    }

    public enum WorkflowStepStatus
    {
        Pending,
        Ready,
        Running,
        Succeeded,
        Failed,
        Skipped,
        NeedsDecision,
        Blocked,
    }

    public enum ToolValidationState
    {
        Available,
        Missing,
        Incomplete,
        NotTested,
    }

    public enum SubtitleSpanishAvailability
    {
        Unknown,
        Present,
        Missing,
    }
}
