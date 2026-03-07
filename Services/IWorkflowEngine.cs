namespace MediaWorkflowOrchestrator.Services
{
    public interface IWorkflowEngine
    {
        WorkflowInstance CreateWorkflow(string selectedPath, bool isFile);
        WorkflowStepState? GetNextReadyStep(WorkflowInstance workflow);
        void ApplyInspectionResult(WorkflowInstance workflow, SubtitleInspectionResult inspectionResult);
        void ApplyTranslationDecision(WorkflowInstance workflow, bool translateRequired);
        void RefreshStatuses(WorkflowInstance workflow);
    }
}
