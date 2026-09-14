namespace MediaWorkflowOrchestrator.Services
{
    public interface IPublicationService
    {
        Task<PublicationWorkspace> PrepareAsync(WorkflowInstance workflow, CancellationToken cancellationToken);
        Task<PublicationWorkspace?> LoadAsync(string workflowId, CancellationToken cancellationToken);
        Task SaveAsync(PublicationWorkspace workspace, CancellationToken cancellationToken);
        Task<PublicationWorkspace> RefreshLinksAsync(PublicationWorkspace workspace, CancellationToken cancellationToken);
        string BuildSheetRows(PublicationWorkspace workspace);
        string BuildSummary(PublicationWorkspace workspace);
    }
}
