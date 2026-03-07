namespace MediaWorkflowOrchestrator.Services
{
    public sealed class ProcessExecutionRequest
    {
        public string FileName { get; init; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
        public string WorkingDirectory { get; init; } = string.Empty;
        public IReadOnlyCollection<int> SuccessExitCodes { get; init; } = new[] { 0 };
    }

    public sealed class ProcessExecutionResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
        public DateTimeOffset StartedAt { get; init; }
        public DateTimeOffset FinishedAt { get; init; }
        public string CommandDisplay { get; init; } = string.Empty;
        public bool Success { get; init; }
    }
}
