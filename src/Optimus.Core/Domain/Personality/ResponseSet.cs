namespace Optimus.Core.Domain.Personality;

/// <summary>Circonstance dans laquelle le copilote prend la parole.</summary>
public enum ResponseEvent
{
    /// <summary>La commande est passée.</summary>
    Success,

    /// <summary>La commande a échoué, ou a été refusée.</summary>
    Fail,

    /// <summary>L'énoncé n'a pas été compris.</summary>
    Unknown,

    /// <summary>Demande de précision ou de confirmation.</summary>
    Clarify,

    /// <summary>Sans circonstance particulière : dialogue, lore, salutation.</summary>
    Any,
}

/// <summary>
/// Conditions qu'une personnalité doit remplir pour qu'une variante soit éligible.
///
/// C'est ce qui permet à une même commande d'avoir une réponse sèche pour un copilote militaire
/// et une pique pour un copilote sarcastique, sans écrire deux catalogues.
/// </summary>
public sealed record ResponseRequirements(
    int? HumorMin = null,
    int? SarcasmMin = null,
    int? FormalityMin = null,
    int? FormalityMax = null,
    SpeechStyle? RequiredStyle = null)
{
    public bool IsSatisfiedBy(Personality personality)
    {
        PersonalityTraits traits = personality.Traits;

        if (HumorMin is int humor && traits.Humor < humor) return false;
        if (SarcasmMin is int sarcasm && traits.Sarcasm < sarcasm) return false;
        if (FormalityMin is int min && traits.Formality < min) return false;
        if (FormalityMax is int max && traits.Formality > max) return false;
        if (RequiredStyle is SpeechStyle style && (personality.Style & style) == 0) return false;

        return true;
    }
}

/// <summary>Une formulation possible.</summary>
/// <param name="Text">Texte, pouvant contenir des variables entre accolades.</param>
/// <param name="Weight">Poids de tirage, relatif aux autres variantes éligibles.</param>
/// <param name="Requires">Conditions d'éligibilité, s'il y en a.</param>
public sealed record ResponseVariant(string Text, double Weight = 1.0, ResponseRequirements? Requires = null);

/// <summary>
/// Répliques d'un copilote, indexées par clé puis par circonstance.
///
/// Plusieurs variantes par entrée n'est pas un luxe : répéter mot pour mot la même phrase à
/// chaque commande est ce qui trahit le plus vite un automate. C'est le levier n°1 du réalisme,
/// avant même la qualité de la voix (docs/08).
/// </summary>
public sealed class ResponseSet
{
    private readonly Dictionary<string, Dictionary<ResponseEvent, List<ResponseVariant>>> _entries;

    public ResponseSet(
        string locale,
        IEnumerable<KeyValuePair<string, Dictionary<ResponseEvent, List<ResponseVariant>>>> entries)
    {
        Locale = locale;
        _entries = new Dictionary<string, Dictionary<ResponseEvent, List<ResponseVariant>>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, Dictionary<ResponseEvent, List<ResponseVariant>>> entry in entries)
        {
            _entries[entry.Key] = entry.Value;
        }
    }

    public string Locale { get; }

    public int EntryCount => _entries.Count;

    public int VariantCount => _entries.Values.Sum(byEvent => byEvent.Values.Sum(list => list.Count));

    /// <summary>Variantes déclarées pour une clé et une circonstance. Vide si rien n'est prévu.</summary>
    public IReadOnlyList<ResponseVariant> Variants(string key, ResponseEvent responseEvent)
    {
        if (!_entries.TryGetValue(key, out Dictionary<ResponseEvent, List<ResponseVariant>>? byEvent))
        {
            return Array.Empty<ResponseVariant>();
        }

        if (byEvent.TryGetValue(responseEvent, out List<ResponseVariant>? exact))
        {
            return exact;
        }

        // « any » sert de repli : une entrée de dialogue n'a pas à distinguer succès et échec.
        return byEvent.TryGetValue(ResponseEvent.Any, out List<ResponseVariant>? fallback)
            ? fallback
            : Array.Empty<ResponseVariant>();
    }

    public bool Contains(string key) => _entries.ContainsKey(key);

    public IReadOnlyCollection<string> Keys => _entries.Keys;

    public static ResponseSet Empty { get; } = new(
        "fr-FR",
        Array.Empty<KeyValuePair<string, Dictionary<ResponseEvent, List<ResponseVariant>>>>());
}
