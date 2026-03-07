namespace MediaWorkflowOrchestrator.Services
{
    public interface IProcessRunnerService
    {
        Task<ProcessExecutionResult> RunAsync(ProcessExecutionRequest request, Action<string>? onOutput, CancellationToken cancellationToken);
    }
}
