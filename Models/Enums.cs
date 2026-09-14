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
        Publish,
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

    public enum PublicationContentKind
    {
        Unknown,
        SeriesEpisode,
        SeriesBatch,
        Movie,
        Collection,
    }
}
