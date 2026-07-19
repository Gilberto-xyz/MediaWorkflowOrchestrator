using System.Text.Json.Serialization;

namespace MediaWorkflowOrchestrator.Models
{
    public sealed class WorkflowStepState : ObservableObject
    {
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush TransparentRowBrush = CreateBrush(0x00, 0x00, 0x00, 0x00);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SubtleRowBorderBrush = CreateBrush(0x22, 0xB6, 0xC5, 0xDA);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedRowBackgroundBrush = CreateBrush(0x52, 0x1C, 0x76, 0x58);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedRowBorderBrush = CreateBrush(0xFF, 0x3F, 0xE6, 0x84);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingBackgroundBrush = CreateBrush(0x1C, 0x90, 0x9D, 0xAE);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingBorderBrush = CreateBrush(0x66, 0x90, 0x9D, 0xAE);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PendingAccentBrush = CreateBrush(0xFF, 0xD7, 0xDF, 0xEA);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ExpectedBackgroundBrush = CreateBrush(0x2E, 0x7A, 0x50, 0x18);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ExpectedBorderBrush = CreateBrush(0x9A, 0xE5, 0xA3, 0x4F);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ExpectedAccentBrush = CreateBrush(0xFF, 0xFF, 0xE4, 0xBF);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SuccessBackgroundBrush = CreateBrush(0x22, 0x4A, 0xD6, 0x6D);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SuccessBorderBrush = CreateBrush(0x88, 0x4A, 0xD6, 0x6D);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SuccessAccentBrush = CreateBrush(0xFF, 0xD9, 0xF9, 0xE5);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SkippedBackgroundBrush = CreateBrush(0x24, 0xF5, 0xC5, 0x42);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SkippedBorderBrush = CreateBrush(0x88, 0xF5, 0xC5, 0x42);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SkippedAccentBrush = CreateBrush(0xFF, 0xFF, 0xF1, 0xBF);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FailedBackgroundBrush = CreateBrush(0x22, 0xF0, 0x71, 0x78);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FailedBorderBrush = CreateBrush(0x88, 0xF0, 0x71, 0x78);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FailedAccentBrush = CreateBrush(0xFF, 0xFF, 0xD6, 0xD9);

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedBorderBrush = CreateBrush(0xFF, 0x3F, 0xE6, 0x84);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedTextBrush = CreateBrush(0xFF, 0xFF, 0xFB, 0xF6);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DefaultTextBrush = CreateBrush(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedSubtitleBrush = CreateBrush(0xEC, 0xF6, 0xE3, 0xC8);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedGlowOuterBrush = CreateBrush(0x4A, 0xE5, 0xA3, 0x4F);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedGlowInnerBrush = CreateBrush(0xA6, 0xF5, 0xC3, 0x72);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedIndicatorBackgroundBrush = CreateBrush(0xFF, 0xFF, 0xE9, 0xCF);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedIndicatorBorderBrush = CreateBrush(0xFF, 0xE5, 0xA3, 0x4F);
        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SelectedRailBrush = CreateBrush(0xFF, 0xF5, 0xC3, 0x72);
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
        public string StepNumber => StepKey switch
        {
            WorkflowStepKey.Download => "00",
            WorkflowStepKey.InspectSubs => "01",
            WorkflowStepKey.TranslateSubs => "02",
            WorkflowStepKey.CleanTracks => "03",
            WorkflowStepKey.TagAndRename => "04",
            WorkflowStepKey.PackageRar => "05",
            _ => "00",
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Controls.Symbol StepSymbol => StepKey switch
        {
            WorkflowStepKey.Download => Microsoft.UI.Xaml.Controls.Symbol.Download,
            WorkflowStepKey.InspectSubs => Microsoft.UI.Xaml.Controls.Symbol.Find,
            WorkflowStepKey.TranslateSubs => Microsoft.UI.Xaml.Controls.Symbol.World,
            WorkflowStepKey.CleanTracks => Microsoft.UI.Xaml.Controls.Symbol.Setting,
            WorkflowStepKey.TagAndRename => Microsoft.UI.Xaml.Controls.Symbol.Page,
            WorkflowStepKey.PackageRar => Microsoft.UI.Xaml.Controls.Symbol.Save,
            _ => Microsoft.UI.Xaml.Controls.Symbol.Forward,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush RowBackgroundBrush => IsSelected ? SelectedRowBackgroundBrush : TransparentRowBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush RowBorderBrush => IsSelected ? SelectedRowBorderBrush : SubtleRowBorderBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush StatusBadgeBackgroundBrush => Status switch
        {
            WorkflowStepStatus.Succeeded => SuccessBackgroundBrush,
            WorkflowStepStatus.Skipped => SkippedBackgroundBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedBackgroundBrush,
            WorkflowStepStatus.Failed => FailedBackgroundBrush,
            _ => PendingBackgroundBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush CardBackgroundBrush => Status switch
        {
            _ when IsSelected => SelectedRowBackgroundBrush,
            WorkflowStepStatus.Succeeded => SuccessBackgroundBrush,
            WorkflowStepStatus.Skipped => SkippedBackgroundBrush,
            WorkflowStepStatus.Ready or WorkflowStepStatus.Running or WorkflowStepStatus.Blocked or WorkflowStepStatus.NeedsDecision => ExpectedBackgroundBrush,
            WorkflowStepStatus.Failed => FailedBackgroundBrush,
            _ => PendingBackgroundBrush,
        };

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush CardBorderBrush => Status switch
        {
            _ when IsSelected => SelectedRowBorderBrush,
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

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush TitleBrush => DefaultTextBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush ReasonBrush => DefaultSubtitleBrush;

        [JsonIgnore]
        public Thickness CardBorderThickness => IsSelected ? new Thickness(2) : new Thickness(1);

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush SelectionGlowOuterBrush => TransparentBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush SelectionGlowInnerBrush => TransparentBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush SelectionIndicatorBackgroundBrush => TransparentBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush SelectionIndicatorBorderBrush => TransparentBrush;

        [JsonIgnore]
        public Microsoft.UI.Xaml.Media.Brush SelectionRailBrush => TransparentBrush;

        [JsonIgnore]
        public double SelectionGlowOpacity => 0;

        [JsonIgnore]
        public Thickness SelectionGlowOuterThickness => new Thickness(0);

        [JsonIgnore]
        public Thickness SelectionGlowInnerThickness => new Thickness(0);

        [JsonIgnore]
        public double SelectionIndicatorOpacity => 0;

        [JsonIgnore]
        public double SelectionRailOpacity => 0;

        private void NotifyVisualStateChanged()
        {
            OnPropertyChanged(nameof(RowBackgroundBrush));
            OnPropertyChanged(nameof(RowBorderBrush));
            OnPropertyChanged(nameof(StatusBadgeBackgroundBrush));
            OnPropertyChanged(nameof(CardBackgroundBrush));
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(StatusAccentBrush));
            OnPropertyChanged(nameof(TitleBrush));
            OnPropertyChanged(nameof(ReasonBrush));
            OnPropertyChanged(nameof(CardBorderThickness));
            OnPropertyChanged(nameof(SelectionGlowOuterBrush));
            OnPropertyChanged(nameof(SelectionGlowInnerBrush));
            OnPropertyChanged(nameof(SelectionGlowOpacity));
            OnPropertyChanged(nameof(SelectionGlowOuterThickness));
            OnPropertyChanged(nameof(SelectionGlowInnerThickness));
            OnPropertyChanged(nameof(SelectionIndicatorBackgroundBrush));
            OnPropertyChanged(nameof(SelectionIndicatorBorderBrush));
            OnPropertyChanged(nameof(SelectionIndicatorOpacity));
            OnPropertyChanged(nameof(SelectionRailBrush));
            OnPropertyChanged(nameof(SelectionRailOpacity));
        }

        private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush TransparentBrush = CreateBrush(0x00, 0x00, 0x00, 0x00);

        private static Microsoft.UI.Xaml.Media.SolidColorBrush CreateBrush(byte a, byte r, byte g, byte b) =>
            new(Microsoft.UI.ColorHelper.FromArgb(a, r, g, b));
    }
}
