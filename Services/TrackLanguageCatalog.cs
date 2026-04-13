using System.Globalization;
using System.Text.Json;

namespace MediaWorkflowOrchestrator.Services
{
    internal static class TrackLanguageCatalog
    {
        private static readonly Lazy<TrackLanguageCatalogData> CachedData = new(LoadData);
        private static readonly CultureInfo SpanishCulture = CultureInfo.GetCultureInfo("es-ES");
        private static readonly IReadOnlyDictionary<string, string> FallbackDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["und"] = "Indefinido",
            ["es"] = "Español",
            ["spa"] = "Español",
            ["es-419"] = "Español (Latinoamérica)",
            ["es-la"] = "Español (Latinoamérica)",
            ["es-es"] = "Español (España)",
            ["en"] = "Inglés",
            ["eng"] = "Inglés",
            ["ja"] = "Japonés",
            ["jpn"] = "Japonés",
            ["ko"] = "Coreano",
            ["kor"] = "Coreano",
            ["zh"] = "Chino",
            ["zho"] = "Chino",
            ["chi"] = "Chino",
            ["fa"] = "Persa",
            ["fas"] = "Persa",
            ["per"] = "Persa",
            ["th"] = "Tailandés",
            ["tha"] = "Tailandés",
        };

        private static readonly IReadOnlyDictionary<string, string> FallbackCanonicalBaseCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["und"] = "und",
            ["es"] = "es",
            ["spa"] = "es",
            ["es-419"] = "es-419",
            ["es-la"] = "es-419",
            ["es-es"] = "es-es",
            ["en"] = "en",
            ["eng"] = "en",
            ["ja"] = "ja",
            ["jpn"] = "ja",
            ["ko"] = "ko",
            ["kor"] = "ko",
            ["zh"] = "zh",
            ["zho"] = "zh",
            ["chi"] = "zh",
            ["fa"] = "fa",
            ["fas"] = "fa",
            ["per"] = "fa",
            ["th"] = "th",
            ["tha"] = "th",
        };

        public static string GetLookupCode(string? languageRaw, string? languageIetf)
        {
            var candidate = PickCandidate(languageRaw, languageIetf);
            return string.IsNullOrWhiteSpace(candidate) ? "und" : NormalizeLanguageVariant(NormalizeCode(candidate));
        }

        public static string GetDisplayName(string? languageRaw, string? languageIetf)
        {
            var lookupCode = GetLookupCode(languageRaw, languageIetf);
            var data = CachedData.Value;

            if (TryGetDisplayName(data.DisplayNames, lookupCode, out var displayName))
            {
                return displayName;
            }

            var baseCode = GetBaseCode(lookupCode);
            if (TryGetDisplayName(data.DisplayNames, baseCode, out displayName))
            {
                return displayName;
            }

            return lookupCode.Length <= 3
                ? lookupCode.ToUpperInvariant()
                : lookupCode;
        }

        public static string GetCanonicalBaseCode(string? languageCode)
        {
            var normalized = NormalizeLanguageVariant(NormalizeCode(languageCode));
            if (string.Equals(normalized, "es", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "spa", StringComparison.OrdinalIgnoreCase))
            {
                return "es";
            }

            if (string.Equals(normalized, "es-es", StringComparison.OrdinalIgnoreCase))
            {
                return "es-es";
            }

            if (normalized.StartsWith("es-", StringComparison.OrdinalIgnoreCase))
            {
                return "es-419";
            }

            var data = CachedData.Value;

            if (TryGetCanonicalCode(data.CanonicalBaseCodes, normalized, out var canonicalCode))
            {
                return canonicalCode;
            }

            var baseCode = GetBaseCode(normalized);
            if (TryGetCanonicalCode(data.CanonicalBaseCodes, baseCode, out canonicalCode))
            {
                return canonicalCode;
            }

            return string.IsNullOrWhiteSpace(baseCode) ? "und" : baseCode;
        }

        private static string PickCandidate(string? languageRaw, string? languageIetf) =>
            !string.IsNullOrWhiteSpace(languageIetf) ? languageIetf! : languageRaw ?? string.Empty;

        private static string NormalizeCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "und";
            }

            return code.Trim().ToLowerInvariant().Replace('_', '-');
        }

        private static string NormalizeLanguageVariant(string normalizedCode) =>
            normalizedCode switch
            {
                "es-la" => "es-419",
                _ => normalizedCode,
            };

        private static string GetBaseCode(string normalizedCode) =>
            normalizedCode.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? normalizedCode;

        private static bool TryGetDisplayName(IReadOnlyDictionary<string, string> displayNames, string code, out string displayName)
        {
            if (displayNames.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                displayName = value;
                return true;
            }

            if (FallbackDisplayNames.TryGetValue(code, out value) && !string.IsNullOrWhiteSpace(value))
            {
                displayName = value;
                return true;
            }

            displayName = string.Empty;
            return false;
        }

        private static bool TryGetCanonicalCode(IReadOnlyDictionary<string, string> canonicalCodes, string code, out string canonicalCode)
        {
            if (canonicalCodes.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                canonicalCode = value;
                return true;
            }

            if (FallbackCanonicalBaseCodes.TryGetValue(code, out value) && !string.IsNullOrWhiteSpace(value))
            {
                canonicalCode = value;
                return true;
            }

            canonicalCode = string.Empty;
            return false;
        }

        private static TrackLanguageCatalogData LoadData()
        {
            var catalogPath = ResolveCatalogPath();
            if (string.IsNullOrWhiteSpace(catalogPath) || !File.Exists(catalogPath))
            {
                return TrackLanguageCatalogData.Empty;
            }

            try
            {
                using var stream = File.OpenRead(catalogPath);
                using var doc = JsonDocument.Parse(stream);
                return new TrackLanguageCatalogData(
                    ParseSection(doc.RootElement, "displayNames"),
                    ParseSection(doc.RootElement, "canonicalBaseCodes"));
            }
            catch
            {
                return TrackLanguageCatalogData.Empty;
            }
        }

        private static string? ResolveCatalogPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Scripts", "track_languages.json"),
                Path.Combine(AppContext.BaseDirectory, "track_languages.json"),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static IReadOnlyDictionary<string, string> ParseSection(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var section) || section.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in section.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var key = NormalizeCode(property.Name);
                var value = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                values[key] = Capitalize(value!);
            }

            return values;
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return string.Concat(
                value[..1].ToUpper(SpanishCulture),
                value.Length > 1 ? value[1..] : string.Empty);
        }

        private sealed record TrackLanguageCatalogData(
            IReadOnlyDictionary<string, string> DisplayNames,
            IReadOnlyDictionary<string, string> CanonicalBaseCodes)
        {
            public static TrackLanguageCatalogData Empty { get; } = new(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }
}
