namespace MediaWorkflowOrchestrator.Services
{
    public interface IWorkflowExecutionService
    {
        Task<AppSettings> GetSettingsAsync();
        Task SaveSettingsAsync(AppSettings settings, string rarPassword);
        Task<AppSettings> RestoreDefaultSettingsAsync();
        string GetDecryptedRarPassword(AppSettings settings);
        Task<IReadOnlyList<ToolValidationResult>> ValidateToolsAsync();
        Task<WorkflowInstance> CreateWorkflowAsync(string selectedPath, bool isFile, CancellationToken cancellationToken);
        Task<WorkflowInstance?> LoadLatestWorkflowAsync();
        Task<IReadOnlyList<WorkflowInstance>> LoadHistoryAsync();
        Task<WorkflowInstance?> LoadWorkflowAsync(string workflowId);
        Task<WorkflowInstance> DecideTranslationAsync(WorkflowInstance workflow, bool translateRequired);
        Task<ExecutionRecord?> ExecuteStepAsync(WorkflowInstance workflow, WorkflowStepKey stepKey, Action<string>? onOutput, CancellationToken cancellationToken, bool forceExecution = false);
        Task<ExecutionRecord?> ExecuteNextReadyStepAsync(WorkflowInstance workflow, Action<string>? onOutput, CancellationToken cancellationToken);
    }
}
