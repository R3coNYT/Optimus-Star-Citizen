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
    /// <param name="directory">Dossier du copilote.</param>
    /// <param name="language">
    /// Langue imposée par le profil. Elle prime sur celle du manifeste : le pilote choisit sa
    /// langue une fois, à l'écran, et tous ses copilotes la suivent. Sans cela, changer de
    /// copilote changerait la langue à son insu.
    /// </param>
    public static LoadResult<Copilot> Load(string directory, string? language = null)
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
        string spoken = Localization.Language.Resolve(language ?? GetString(root, "language"));
        string wakeWord = GetString(root, "wake_word") ?? name;

        VoiceConfig voice = ReadVoice(root);

        Domain.Personality.Personality personality = ReadPersonality(
            Path.Combine(directory, GetString(root, "personality_ref") ?? "personality.json"),
            issues);

        // « responses_ref » reste un passe-droit pour un copilote qui tiendrait à un fichier
        // nommé autrement ; sans lui, le fichier se déduit de la langue, avec repli.
        //
        // Mais « responses.fr.json » n'est PAS un autre nom : c'est le nom par défaut, et les
        // fiches livrées le portaient en toutes lettres jusqu'au 2026-08-28. L'installateur
        // préservant la fiche du pilote — elle contient sa voix et son mot d'éveil — ce champ
        // survit à la mise à jour et épingle le français à vie.
        //
        // Mesuré le 2026-08-29 sur le poste de jeu : catalogue anglais, répliques françaises.
        // « Optimus, open the doors » reconnu à 0,66, exécuté, et la réponse « Sas ouverts ».
        //
        // Un champ retiré d'un fichier livré ne disparaît pas des machines : c'est la leçon de
        // D35, D43, D46, D70 et D77 vue depuis l'autre bout. Le code doit donc savoir lire ce
        // qu'il n'écrit plus.
        string? reference = GetString(root, "responses_ref");
        bool legacyPin = reference is not null && IsDefaultResponseName(reference);

        string responsePath =
            reference is not null && !legacyPin
                ? Path.Combine(directory, reference)
                : Localization.Language.Localized(directory, "responses", ".json", spoken)
                  ?? Path.Combine(directory, "responses.fr.json");

        // Le repli était MUET, et c'est ce qui l'a rendu coûteux. Mesuré le 2026-08-29 sur le
        // poste de jeu : le catalogue passait bien à l'anglais, les répliques restaient
        // françaises, et le journal n'en disait pas un mot. Optimus reconnaissait « open the
        // doors » pour répondre « Sas ouverts », et rien n'expliquait pourquoi.
        //
        // Un copilote copié par le pilote suffit à provoquer le cas : sa copie masque celle
        // qui est livrée, fichiers non modifiés compris, et elle date d'avant la traduction.
        string expected = $"responses.{Localization.Language.Short(spoken)}.json";

        if (!string.Equals(Path.GetFileName(responsePath), expected, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new LoadIssue(directory, "responses", $"{expected} est absent."));

            // Au journal aussi, et pas seulement dans les anomalies de chargement : c'est un
            // symptome qu'on entend avant de le lire, et il faut pouvoir remonter du son a
            // la cause sans ouvrir l'ecran des donnees.
            Diagnostics.DiagnosticLog.Warn(
                $"no replies in “{spoken}” for copilot “{id}”",
                $"{expected} is missing from {directory}. "
                + $"Optimus will speak with {Path.GetFileName(responsePath)}.");
        }

        ResponseSet responses = ReadResponses(responsePath, issues, out Lexicon? spokenLexicon);

        // Le lexique des REPLIQUES l'emporte sur celui du caractere, quand il existe.
        //
        // Les formes d'adresse sont de la langue — « commandant » n'a pas d'équivalent dans un
        // fichier de curseurs — tandis que l'humour et la formalité n'en sont pas. Les laisser
        // ensemble aurait fait dire à un copilote anglais « At your orders, commandant », ou
        // imposé de dupliquer les huit curseurs dans un personality.en.json que le pilote
        // aurait ensuite édité à moitié.
        if (spokenLexicon is not null)
        {
            personality = personality with { Lexicon = spokenLexicon };
        }

        Copilot copilot = new(
            id, name, spoken, wakeWord, voice, personality, responses,
            GetString(root, "description"),
            GetString(root, "accent_color"));

        return new LoadResult<Copilot>(copilot, issues);
    }

    /// <summary>
    /// Ce nom est-il celui que le chargeur aurait choisi tout seul ?
    ///
    /// « responses.json », « responses.fr.json », « responses.en.json » : dans ces trois cas
    /// « responses_ref » n'exprime aucune volonté, il répète la règle par défaut d'une époque
    /// où il n'y avait qu'une langue. Un copilote qui tient vraiment à « les-repliques.json »
    /// garde, lui, son passe-droit.
    /// </summary>
    private static bool IsDefaultResponseName(string reference)
    {
        const string prefix = "responses.";
        const string suffix = ".json";

        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !reference.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            || reference.Length < prefix.Length + suffix.Length)
        {
            return string.Equals(reference, "responses.json", StringComparison.OrdinalIgnoreCase);
        }

        string middle = reference[prefix.Length..^suffix.Length];

        return middle.Length == 2
            && char.IsAsciiLetter(middle[0])
            && char.IsAsciiLetter(middle[1]);
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
            issues.Add(new LoadIssue(path, null, "Personality not found, defaults applied."));
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

        Lexicon lexicon = ReadLexicon(root) ?? Lexicon.Empty;

        return new Domain.Personality.Personality(
            traits,
            lexicon,
            style == SpeechStyle.None ? SpeechStyle.Immersive : style,
            ReadRules(root, path, issues));
    }

    /// <summary>
    /// Lit un lexique, d'où qu'il vienne, ou <c>null</c> s'il n'y en a pas.
    ///
    /// Extraite parce qu'elle sert deux fois : le caractère en porte un, et les répliques
    /// peuvent en porter un autre. Voir <see cref="Load"/> pour savoir lequel l'emporte.
    /// </summary>
    private static Lexicon? ReadLexicon(JsonElement root)
    {
        if (!root.TryGetProperty("lexicon", out JsonElement lexiconElement) ||
            lexiconElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

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

        return new Lexicon(
            GetStringArray(lexiconElement, "address_user"),
            GetStringArray(lexiconElement, "forbidden_phrases"),
            replacements);
    }

    /// <summary>
    /// Lit les règles de comportement. Une règle mal formée est écartée et signalée : mieux
    /// vaut un copilote qui adapte moins qu'un copilote qui refuse de démarrer.
    /// </summary>
    private static List<BehaviorRule> ReadRules(JsonElement root, string path, List<LoadIssue> issues)
    {
        List<BehaviorRule> rules = new();

        if (!root.TryGetProperty("rules", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
        {
            return rules;
        }

        foreach (JsonElement element in array.EnumerateArray())
        {
            string? when = GetString(element, "when");
            string? behavior = GetString(element, "behavior");

            if (!TryParseTrigger(when, out BehaviorTrigger trigger))
            {
                issues.Add(new LoadIssue(path, "rules", $"Unknown circumstance “{when}”, rule ignored."));
                continue;
            }

            if (!TryParseEffect(behavior, out BehaviorEffect effect))
            {
                issues.Add(new LoadIssue(path, "rules", $"Unknown behaviour “{behavior}”, rule ignored."));
                continue;
            }

            rules.Add(new BehaviorRule(
                trigger,
                effect,
                GetInt(element, "priority") ?? 50,
                GetInt(element, "threshold") ?? 0,
                GetInt(element, "max_words"),
                GetString(element, "response_key")));
        }

        return rules;
    }

    private static bool TryParseTrigger(string? value, out BehaviorTrigger trigger)
    {
        switch (value?.ToLowerInvariant())
        {
            case "combat_active": trigger = BehaviorTrigger.CombatActive; return true;
            case "command_failed": trigger = BehaviorTrigger.CommandFailed; return true;
            case "repeated_failure": trigger = BehaviorTrigger.RepeatedFailure; return true;
            case "command_unknown": trigger = BehaviorTrigger.CommandUnknown; return true;
            case "idle_long": trigger = BehaviorTrigger.IdleLong; return true;
            case "startup": trigger = BehaviorTrigger.Startup; return true;
            default: trigger = BehaviorTrigger.CombatActive; return false;
        }
    }

    private static bool TryParseEffect(string? value, out BehaviorEffect effect)
    {
        switch (value?.ToLowerInvariant())
        {
            case "short_responses": effect = BehaviorEffect.ShortResponses; return true;
            case "explain_reason": effect = BehaviorEffect.ExplainReason; return true;
            case "suggest_fix": effect = BehaviorEffect.SuggestFix; return true;
            case "stay_neutral": effect = BehaviorEffect.StayNeutral; return true;
            case "speak": effect = BehaviorEffect.Speak; return true;
            default: effect = BehaviorEffect.StayNeutral; return false;
        }
    }

    private static ResponseSet ReadResponses(
        string path, List<LoadIssue> issues, out Lexicon? lexicon)
    {
        lexicon = null;

        if (!File.Exists(path))
        {
            issues.Add(new LoadIssue(path, null, "Replies not found: the copilot will stay silent."));
            return ResponseSet.Empty;
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        lexicon = ReadLexicon(root);
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
                    issues.Add(new LoadIssue(path, entry.Name, $"Unknown circumstance “{eventProperty.Name}”, ignored."));
                    continue;
                }

                List<ResponseVariant> variants = new();

                foreach (JsonElement variantElement in eventProperty.Value.EnumerateArray())
                {
                    string? text = GetString(variantElement, "text");
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        issues.Add(new LoadIssue(path, entry.Name, "Variant with no text, ignored."));
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
