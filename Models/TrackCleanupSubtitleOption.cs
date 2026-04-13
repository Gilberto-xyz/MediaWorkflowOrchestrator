using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;

namespace MediaWorkflowOrchestrator.Models
{
    public sealed class TrackCleanupSubtitleOption : ObservableObject
    {
        private string trackId = string.Empty;
        private string languageCode = "und";
        private string languageLabel = "UND";
        private string name = string.Empty;
        private bool isDefault;
        private bool isForced;
        private bool isPrimary;
        private bool isSelected = true;

        public string TrackId
        {
            get => trackId;
            set
            {
                if (SetProperty(ref trackId, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        public string LanguageCode
        {
            get => languageCode;
            set => SetProperty(ref languageCode, value);
        }

        public string LanguageLabel
        {
            get => languageLabel;
            set
            {
                if (SetProperty(ref languageLabel, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        public string Name
        {
            get => name;
            set
            {
                if (SetProperty(ref name, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        public bool IsDefault
        {
            get => isDefault;
            set
            {
                if (SetProperty(ref isDefault, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        public bool IsForced
        {
            get => isForced;
            set
            {
                if (SetProperty(ref isForced, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        public bool IsPrimary
        {
            get => isPrimary;
            set
            {
                if (SetProperty(ref isPrimary, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value))
                {
                    NotifyPresentationChanged();
                }
            }
        }

        [JsonIgnore]
        public string PrimaryLabel
        {
            get
            {
                var parts = new List<string>
                {
                    $"#{TrackId}",
                    LanguageLabel,
                };

                if (IsPrimary)
                {
                    parts.Add("Principal");
                }

                if (IsForced)
                {
                    parts.Add("Forzados");
                }

                if (IsDefault)
                {
                    parts.Add("Default origen");
                }

                return string.Join(" · ", parts);
            }
        }

        [JsonIgnore]
        public string SecondaryLabel => string.IsNullOrWhiteSpace(Name) ? "Subtítulo sin nombre" : Name;

        [JsonIgnore]
        public Brush CardBackgroundBrush => TrackCleanupOptionVisuals.GetBackground(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush CardBorderBrush => TrackCleanupOptionVisuals.GetBorder(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush TitleBrush => TrackCleanupOptionVisuals.GetTitleBrush(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush CaptionBrush => TrackCleanupOptionVisuals.GetCaptionBrush(IsSelected, IsPrimary);

        [JsonIgnore]
        public string PrimaryActionLabel => IsPrimary ? "Principal actual" : "Principal";

        private void NotifyPresentationChanged()
        {
            OnPropertyChanged(nameof(PrimaryLabel));
            OnPropertyChanged(nameof(SecondaryLabel));
            OnPropertyChanged(nameof(CardBackgroundBrush));
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(TitleBrush));
            OnPropertyChanged(nameof(CaptionBrush));
            OnPropertyChanged(nameof(PrimaryActionLabel));
        }
    }
}
