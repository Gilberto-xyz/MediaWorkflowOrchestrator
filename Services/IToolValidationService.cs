namespace MediaWorkflowOrchestrator.Services
{
    public interface IToolValidationService
    {
        Task<IReadOnlyList<ToolValidationResult>> ValidateAllAsync(AppSettings settings);
    }
}
