using System.Text.Json.Serialization;

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

        public bool IsForced
        {
            get => isForced;
            set => SetProperty(ref isForced, value);
        }

        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
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
    }
}
