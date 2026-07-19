using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media;

namespace MediaWorkflowOrchestrator.Models
{
    public sealed class TrackCleanupAudioOption : ObservableObject
    {
        private string trackId = string.Empty;
        private string languageCode = "und";
        private string languageLabel = "UND";
        private string codec = string.Empty;
        private string name = string.Empty;
        private bool isDefault;
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
            set
            {
                if (SetProperty(ref languageCode, NormalizeLanguageCode(value)))
                {
                    var label = TrackLanguageCatalog.GetDisplayName(languageCode, languageCode);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        LanguageLabel = label;
                    }

                    NotifyPresentationChanged();
                }
            }
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

        public string Codec
        {
            get => codec;
            set
            {
                if (SetProperty(ref codec, value))
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

                if (IsDefault)
                {
                    parts.Add("Default origen");
                }

                return string.Join(" · ", parts);
            }
        }

        [JsonIgnore]
        public string SecondaryLabel
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Codec))
                {
                    parts.Add(Codec);
                }

                if (!string.IsNullOrWhiteSpace(Name))
                {
                    parts.Add(Name);
                }

                return parts.Count > 0 ? string.Join(" · ", parts) : "Pista sin nombre";
            }
        }

        [JsonIgnore]
        public Brush CardBackgroundBrush => TrackCleanupOptionVisuals.GetBackground(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush CardBorderBrush => TrackCleanupOptionVisuals.GetBorder(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush TitleBrush => TrackCleanupOptionVisuals.GetTitleBrush(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush CaptionBrush => TrackCleanupOptionVisuals.GetCaptionBrush(IsSelected, IsPrimary);

        [JsonIgnore]
        public Brush SelectorBackgroundBrush => TrackCleanupOptionVisuals.GetSelectorBackground(IsSelected);

        [JsonIgnore]
        public Brush SelectorBorderBrush => TrackCleanupOptionVisuals.GetSelectorBorder(IsSelected);

        [JsonIgnore]
        public Brush SelectorGlyphBrush => TrackCleanupOptionVisuals.GetSelectorGlyph(IsSelected);

        [JsonIgnore]
        public string PrimaryActionLabel => IsPrimary ? "Principal actual" : "Principal";

        [JsonIgnore]
        public string PrimaryBadgeLabel => IsPrimary ? "Actual" : "Principal";

        private void NotifyPresentationChanged()
        {
            OnPropertyChanged(nameof(PrimaryLabel));
            OnPropertyChanged(nameof(SecondaryLabel));
            OnPropertyChanged(nameof(CardBackgroundBrush));
            OnPropertyChanged(nameof(CardBorderBrush));
            OnPropertyChanged(nameof(TitleBrush));
            OnPropertyChanged(nameof(CaptionBrush));
            OnPropertyChanged(nameof(SelectorBackgroundBrush));
            OnPropertyChanged(nameof(SelectorBorderBrush));
            OnPropertyChanged(nameof(SelectorGlyphBrush));
            OnPropertyChanged(nameof(PrimaryActionLabel));
            OnPropertyChanged(nameof(PrimaryBadgeLabel));
        }

        private static string NormalizeLanguageCode(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? "und"
                : value.Trim().Replace('_', '-').ToLowerInvariant();
    }
}
