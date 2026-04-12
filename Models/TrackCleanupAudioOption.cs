using System.Text.Json.Serialization;

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
        private bool isSelected = true;

        public string TrackId
        {
            get => trackId;
            set => SetProperty(ref trackId, value);
        }

        public string LanguageCode
        {
            get => languageCode;
            set => SetProperty(ref languageCode, value);
        }

        public string LanguageLabel
        {
            get => languageLabel;
            set => SetProperty(ref languageLabel, value);
        }

        public string Codec
        {
            get => codec;
            set => SetProperty(ref codec, value);
        }

        public string Name
        {
            get => name;
            set => SetProperty(ref name, value);
        }

        public bool IsDefault
        {
            get => isDefault;
            set => SetProperty(ref isDefault, value);
        }

        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }

        [JsonIgnore]
        public string PrimaryLabel => IsDefault
            ? $"#{TrackId} · {LanguageLabel} · Default origen"
            : $"#{TrackId} · {LanguageLabel}";

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
    }
}
