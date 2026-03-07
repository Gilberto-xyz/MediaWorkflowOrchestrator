namespace MediaWorkflowOrchestrator.Services
{
    public interface ISubtitleInspectorService
    {
        Task<SubtitleInspectionResult> InspectAsync(string path, AppSettings settings, CancellationToken cancellationToken);
    }
}
