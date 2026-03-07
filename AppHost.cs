namespace MediaWorkflowOrchestrator
{
    public sealed class AppHost
    {
        public AppHost()
        {
            SecretProtector = new DpapiSecretProtector();
            AppSettingsService = new AppSettingsService();
            WorkflowStore = new WorkflowStore();
            ProcessRunnerService = new ProcessRunnerService();
            ToolValidationService = new ToolValidationService();
            SubtitleInspectorService = new SubtitleInspectorService(ProcessRunnerService);
            WorkflowEngine = new WorkflowEngine();
            WorkflowExecutionService = new WorkflowExecutionService(
                AppSettingsService,
                WorkflowStore,
                SecretProtector,
                ProcessRunnerService,
                ToolValidationService,
                SubtitleInspectorService,
                WorkflowEngine);
        }

        public ISecretProtector SecretProtector { get; }
        public IAppSettingsService AppSettingsService { get; }
        public IWorkflowStore WorkflowStore { get; }
        public IProcessRunnerService ProcessRunnerService { get; }
        public IToolValidationService ToolValidationService { get; }
        public ISubtitleInspectorService SubtitleInspectorService { get; }
        public IWorkflowEngine WorkflowEngine { get; }
        public IWorkflowExecutionService WorkflowExecutionService { get; }
    }
}
