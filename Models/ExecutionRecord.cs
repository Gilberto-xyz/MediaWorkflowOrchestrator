namespace MediaWorkflowOrchestrator.Models
{
    public sealed class ExecutionRecord
    {
        public string WorkflowId { get; set; } = string.Empty;
        public WorkflowStepKey StepKey { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset FinishedAt { get; set; }
        public int ExitCode { get; set; }
        public string CommandDisplay { get; set; } = string.Empty;
        public string WorkingDirectory { get; set; } = string.Empty;
        public string StdoutLogPath { get; set; } = string.Empty;
        public string StderrLogPath { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Summary { get; set; } = string.Empty;
    }
}
