using System.Text.Json.Serialization;

namespace MediaWorkflowOrchestrator.Models
{
    public sealed class WorkflowStepState : ObservableObject
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

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedBackgroundBrush = CreateBrush(0x33, 0xD9, 0x77, 0x06);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedBorderBrush = CreateBrush(0xFF, 0xD9, 0x77, 0x06);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedAccentBrush = CreateBrush(0xFF, 0xFF, 0xE1, 0xBC);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedTextBrush = CreateBrush(0xFF, 0xFF, 0xF7, 0xED);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DefaultTextBrush = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedSubtitleBrush = CreateBrush(0xEE, 0xFF, 0xEA, 0xCF);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DefaultSubtitleBrush = CreateBrush(0xB8, 0xFF, 0xFF, 0xFF);

        private WorkflowStepKey stepKey;
        private string displayName = string.Empty;
        private WorkflowStepStatus status = WorkflowStepStatus.Pending;
        private string statusReason = string.Empty;
        private DateTimeOffset? startedAt;
        private DateTimeOffset? finishedAt;
        private int? exitCode;
        private string stdoutLogPath = string.Empty;
        private string stderrLogPath = string.Empty;
        private string userDecision = string.Empty;
        private Dictionary<string, string> outputHints = new();
        private bool isSelected;

        public WorkflowStepKey StepKey
        {
            get => stepKey;
            set => SetProperty(ref stepKey, value);
        }

        public string DisplayName
        {
            get => displayName;
            set => SetProperty(ref displayName, value);
        }

        public WorkflowStepStatus Status
        {
            get => status;
            set
            {
                if (SetProperty(ref status, value))
                {
                    NotifyVisualStateChanged();
                }
            }
        }

        public string StatusReason
        {
            get => statusReason;
            set => SetProperty(ref statusReason, value);
        }

        public DateTimeOffset? StartedAt
        {
            get => startedAt;
            set => SetProperty(ref startedAt, value);
        }

        public DateTimeOffset? FinishedAt
        {
            get => finishedAt;
            set
            {
                if (SetProperty(ref finishedAt, value))
                {
                    OnPropertyChanged(nameof(FinishedAtDisplay));
                }
            }
        }

        public int? ExitCode
        {
            get => exitCode;
            set => SetProperty(ref exitCode, value);
        }

        public string StdoutLogPath
        {
            get => stdoutLogPath;
            set => SetProperty(ref stdoutLogPath, value);
        }

        public string StderrLogPath
        {
            get => stderrLogPath;
            set => SetProperty(ref stderrLogPath, value);
        }

        public string UserDecision
        {
            get => userDecision;
            set => SetProperty(ref userDecision, value);
        }

        public Dictionary<string, string> OutputHints
        {
            get => outputHints;
            set => SetProperty(ref outputHints, value);
        }

        [JsonIgnore]
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value))
                {
                    NotifyVisualStateChanged();
                }
            }
        }

        [JsonIgnore]
        public string FinishedAtDisplay => FinishedAt?.ToLocalTime().ToString("g") ?? "Pendiente";

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush CardBackgroundBrush => Status switch
        {
            _ when IsSelected => SelectedBackgroundBrush,
            WorkflowStepStatus.Succeeded => SuccessBackgroundBrush,
            WorkflowStepStatus.Skipped => SkippedBackgroundBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedBackgroundBrush,
            WorkflowStepStatus.Failed => FailedBackgroundBrush,
            _ => PendingBackgroundBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush CardBorderBrush => Status switch
        {
            _ when IsSelected => SelectedBorderBrush,
            WorkflowStepStatus.Succeeded => SuccessBorderBrush,
            WorkflowStepStatus.Skipped => SkippedBorderBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedBorderBrush,
            WorkflowStepStatus.Failed => FailedBorderBrush,
            _ => PendingBorderBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush StatusAccentBrush => Status switch
        {
            _ when IsSelected => SelectedAccentBrush,
            WorkflowStepStatus.Succeeded => SuccessAccentBrush,
            WorkflowStepStatus.Skipped => SkippedAccentBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedAccentBrush,
            WorkflowStepStatus.Failed => FailedAccentBrush,
            _ => PendingAccentBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush TitleBrush => IsSelected ? SelectedTextBrush : DefaultTextBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush ReasonBrush => IsSelected ? SelectedSubtitleBrush : DefaultSubtitleBrush;

        [JsonIgnore]
        public Thickness CardBorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

        [JsonIgnore]
        public string SelectionLabel => IsSelected ? "Seleccionado" : string.Empty;

        private void NotifyVisualStateChanged()
        {
            OnPropertyChanged(nameof(CardBackgroundBrush));
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(StatusAccentBrush));
            OnPropertyChanged(nameof(TitleBrush));
            OnPropertyChanged(nameof(ReasonBrush));
            OnPropertyChanged(nameof(CardBorderThickness));
            OnPropertyChanged(nameof(SelectionLabel));
        }

        private static Microsoft.UI.Xaml.Media.SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }
}
