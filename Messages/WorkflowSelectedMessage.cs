using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MediaWorkflowOrchestrator.Messages
{
    public sealed class WorkflowSelectedMessage : ValueChangedMessage<string>
    {
        public WorkflowSelectedMessage(string value) : base(value)
        {
        }
    }
}
