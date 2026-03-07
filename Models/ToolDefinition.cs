namespace MediaWorkflowOrchestrator.Models
{
    public sealed class ToolDefinition
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? WorkingDirectory { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
