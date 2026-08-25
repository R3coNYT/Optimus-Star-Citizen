using System.Text.Json;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;

namespace Optimus.Core.Loading;

/// <summary>
/// Chargement d'un copilote depuis son dossier : identité, personnalité, répliques.
///
/// Même principe que pour les catalogues : un élément invalide est écarté et signalé, jamais
/// bloquant. Une variante de réponse mal formée ne doit pas priver l'utilisateur de son
/// copilote — elle doit être visible dans les anomalies, et c'est tout.
/// </summary>
public static class CopilotLoader
{
    /// <summary>Charge le copilote contenu dans <paramref name="directory"/>.</summary>
    public static LoadResult<Copilot> Load(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        List<LoadIssue> issues = new();
        string manifestPath = Path.Combine(directory, "copilot.json");

        if (!File.Exists(manifestPath))
        {
            issues.Add(new LoadIssue(directory, null, "copilot.json introuvable."));
            return new LoadResult<Copilot>(Copilot.Fallback, issues);
        }

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = manifest.RootElement;

        string id = GetString(root, "id") ?? Path.GetFileName(directory);
        string name = GetString(root, "name") ?? id;
        string language = GetString(root, "language") ?? "fr-FR";
        string wakeWord = GetString(root, "wake_word") ?? name;

        VoiceConfig voice = ReadVoice(root);

        Domain.Personality.Personality personality = ReadPersonality(
            Path.Combine(directory, GetString(root, "personality_ref") ?? "personality.json"),
            issues);

        ResponseSet responses = ReadResponses(
            Path.Combine(directory, GetString(root, "responses_ref") ?? $"responses.{language[..2]}.json"),
            issues);

        Copilot copilot = new(
            id, name, language, wakeWord, voice, personality, responses,
            GetString(root, "description"),
            GetString(root, "accent_color"));

        return new LoadResult<Copilot>(copilot, issues);
    }

    private static VoiceConfig ReadVoice(JsonElement root)
    {
        if (!root.TryGetProperty("voice", out JsonElement voice) || voice.ValueKind != JsonValueKind.Object)
        {
            return new VoiceConfig();
        }

        return new VoiceConfig(
            GetString(voice, "provider") ?? "windows-onecore",
            GetString(voice, "voice_id"),
            GetDouble(voice, "rate") ?? 1.0,
            GetDouble(voice, "volume") ?? 0.9);
    }

    private static Domain.Personality.Personality ReadPersonality(string path, List<LoadIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(new LoadIssue(path, null, "Personnalité introuvable, valeurs par défaut appliquées."));
            return Domain.Personality.Personality.Default;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        PersonalityTraits traits = new PersonalityTraits(
            GetInt(root, "traits", "humor") ?? 40,
            GetInt(root, "traits", "sarcasm") ?? 25,
            GetInt(root, "traits", "formality") ?? 80,
            GetInt(root, "traits", "verbosity") ?? 30,
            GetInt(root, "traits", "aggression") ?? 10,
            GetInt(root, "traits", "calmness") ?? 90,
            GetInt(root, "traits", "warmth") ?? 45,
            GetInt(root, "traits", "confidence") ?? 85).Clamped();

        SpeechStyle style = SpeechStyle.None;
        foreach (string flag in GetStringArray(root, "style"))
        {
            style |= flag.ToLowerInvariant() switch
            {
                "military" => SpeechStyle.Military,
                "sci_fi" or "scifi" => SpeechStyle.SciFi,
                "immersive" => SpeechStyle.Immersive,
                "technical" => SpeechStyle.Technical,
                _ => SpeechStyle.None,
            };
        }

        Lexicon lexicon = Lexicon.Empty;

        if (root.TryGetProperty("lexicon", out JsonElement lexiconElement) &&
            lexiconElement.ValueKind == JsonValueKind.Object)
        {
            Dictionary<string, string> replacements = new(StringComparer.OrdinalIgnoreCase);

            if (lexiconElement.TryGetProperty("replacements", out JsonElement replacementsElement) &&
                replacementsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in replacementsElement.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        replacements[property.Name] = property.Value.GetString() ?? string.Empty;
                    }
                }
            }

            lexicon = new Lexicon(
                GetStringArray(lexiconElement, "address_user"),
                GetStringArray(lexiconElement, "forbidden_phrases"),
                replacements);
        }

        return new Domain.Personality.Personality(
            traits,
            lexicon,
            style == SpeechStyle.None ? SpeechStyle.Immersive : style);
    }

    private static ResponseSet ReadResponses(string path, List<LoadIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(new LoadIssue(path, null, "Répliques introuvables : le copilote restera muet."));
            return ResponseSet.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        string locale = GetString(root, "locale") ?? "fr-FR";

        List<KeyValuePair<string, Dictionary<ResponseEvent, List<ResponseVariant>>>> entries = new();

        if (!root.TryGetProperty("entries", out JsonElement entriesElement) ||
            entriesElement.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new LoadIssue(path, null, "Aucun objet « entries »."));
            return ResponseSet.Empty;
        }

        foreach (JsonProperty entry in entriesElement.EnumerateObject())
        {
            Dictionary<ResponseEvent, List<ResponseVariant>> byEvent = new();

            foreach (JsonProperty eventProperty in entry.Value.EnumerateObject())
            {
                if (!TryParseEvent(eventProperty.Name, out ResponseEvent responseEvent))
                {
                    issues.Add(new LoadIssue(path, entry.Name, $"Circonstance « {eventProperty.Name} » inconnue, ignorée."));
                    continue;
                }

                List<ResponseVariant> variants = new();

                foreach (JsonElement variantElement in eventProperty.Value.EnumerateArray())
                {
                    string? text = GetString(variantElement, "text");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        issues.Add(new LoadIssue(path, entry.Name, "Variante sans texte, ignorée."));
                        continue;
                    }

                    variants.Add(new ResponseVariant(
                        text,
                        GetDouble(variantElement, "weight") ?? 1.0,
                        ReadRequirements(variantElement)));
                }

                if (variants.Count > 0)
                {
                    byEvent[responseEvent] = variants;
                }
            }

            if (byEvent.Count > 0)
            {
                entries.Add(new KeyValuePair<string, Dictionary<ResponseEvent, List<ResponseVariant>>>(entry.Name, byEvent));
            }
        }

        return new ResponseSet(locale, entries);
    }

    private static ResponseRequirements? ReadRequirements(JsonElement variant)
    {
        if (!variant.TryGetProperty("requires", out JsonElement requires) ||
            requires.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ResponseRequirements(
            GetInt(requires, "humor_min"),
            GetInt(requires, "sarcasm_min"),
            GetInt(requires, "formality_min"),
            GetInt(requires, "formality_max"));
    }

    private static bool TryParseEvent(string value, out ResponseEvent responseEvent)
    {
        switch (value.ToLowerInvariant())
        {
            case "success": responseEvent = ResponseEvent.Success; return true;
            case "fail": responseEvent = ResponseEvent.Fail; return true;
            case "unknown": responseEvent = ResponseEvent.Unknown; return true;
            case "clarify": responseEvent = ResponseEvent.Clarify; return true;
            case "any": responseEvent = ResponseEvent.Any; return true;
            default: responseEvent = ResponseEvent.Any; return false;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number)
            ? number
            : null;

    private static int? GetInt(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out JsonElement container) ? GetInt(container, property) : null;

    private static double? GetDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number)
            ? number
            : null;

    private static List<string> GetStringArray(JsonElement element, string property)
    {
        List<string> values = new();

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out JsonElement array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text);
                    }
                }
            }
        }

        return values;
    }
}
