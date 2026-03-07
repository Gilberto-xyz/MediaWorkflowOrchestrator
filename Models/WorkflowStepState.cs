using System.Text.Json.Serialization;

namespace MediaWorkflowOrchestrator.Models
{
    public sealed class WorkflowStepState
    {
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingBackgroundBrush = CreateBrush(0x1A, 0x94, 0xA3, 0xB8);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingBorderBrush = CreateBrush(0x66, 0x94, 0xA3, 0xB8);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingAccentBrush = CreateBrush(0xFF, 0xD1, 0xD5, 0xDB);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ExpectedBackgroundBrush = CreateBrush(0x20, 0xC4, 0xB5, 0xFD);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ExpectedBorderBrush = CreateBrush(0x80, 0xC4, 0xB5, 0xFD);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ExpectedAccentBrush = CreateBrush(0xFF, 0xE9, 0xDD, 0xFF);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SuccessBackgroundBrush = CreateBrush(0x22, 0x4A, 0xD6, 0x6D);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SuccessBorderBrush = CreateBrush(0x88, 0x4A, 0xD6, 0x6D);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SuccessAccentBrush = CreateBrush(0xFF, 0xD9, 0xF9, 0xE5);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SkippedBackgroundBrush = CreateBrush(0x24, 0xF5, 0xC5, 0x42);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SkippedBorderBrush = CreateBrush(0x88, 0xF5, 0xC5, 0x42);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SkippedAccentBrush = CreateBrush(0xFF, 0xFF, 0xF1, 0xBF);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FailedBackgroundBrush = CreateBrush(0x22, 0xF0, 0x71, 0x78);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FailedBorderBrush = CreateBrush(0x88, 0xF0, 0x71, 0x78);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FailedAccentBrush = CreateBrush(0xFF, 0xFF, 0xD6, 0xD9);

        public WorkflowStepKey StepKey { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public WorkflowStepStatus Status { get; set; } = WorkflowStepStatus.Pending;
        public string StatusReason { get; set; } = string.Empty;
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public int? ExitCode { get; set; }
        public string StdoutLogPath { get; set; } = string.Empty;
        public string StderrLogPath { get; set; } = string.Empty;
        public string UserDecision { get; set; } = string.Empty;
        public Dictionary<string, string> OutputHints { get; set; } = new();

        [JsonIgnore]
        public string FinishedAtDisplay => FinishedAt?.ToLocalTime().ToString("g") ?? "Pendiente";

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush CardBackgroundBrush => Status switch
        {
            WorkflowStepStatus.Succeeded => SuccessBackgroundBrush,
            WorkflowStepStatus.Skipped => SkippedBackgroundBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedBackgroundBrush,
            WorkflowStepStatus.Failed => FailedBackgroundBrush,
            _ => PendingBackgroundBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush CardBorderBrush => Status switch
        {
            WorkflowStepStatus.Succeeded => SuccessBorderBrush,
            WorkflowStepStatus.Skipped => SkippedBorderBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedBorderBrush,
            WorkflowStepStatus.Failed => FailedBorderBrush,
            _ => PendingBorderBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush StatusAccentBrush => Status switch
        {
            WorkflowStepStatus.Succeeded => SuccessAccentBrush,
            WorkflowStepStatus.Skipped => SkippedAccentBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedAccentBrush,
            WorkflowStepStatus.Failed => FailedAccentBrush,
            _ => PendingAccentBrush,
        };

        private static Microsoft.UI.Xaml.Media.SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }
}
