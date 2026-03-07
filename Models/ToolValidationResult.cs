namespace MediaWorkflowOrchestrator.Models
{
    public sealed class ToolValidationResult
    {
        public string ToolKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ToolValidationState State { get; set; } = ToolValidationState.NotTested;
        public string Message { get; set; } = string.Empty;
        public string? Path { get; set; }

        public string StateDisplay => State switch
        {
            ToolValidationState.Available => "Disponible",
            ToolValidationState.Missing => "Falta",
            ToolValidationState.Incomplete => "Configuración incompleta",
            _ => "No probado",
        };
    }
}
