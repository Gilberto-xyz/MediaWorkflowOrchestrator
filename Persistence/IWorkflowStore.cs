using MediaWorkflowOrchestrator.Models;

namespace MediaWorkflowOrchestrator.Persistence
{
    public interface IWorkflowStore
    {
        Task SaveAsync(WorkflowInstance workflow);
        Task<WorkflowInstance?> LoadAsync(string workflowId);
        Task<IReadOnlyList<WorkflowInstance>> LoadAllAsync();
        Task<WorkflowInstance?> LoadLatestAsync();
    }
}
