using System.Text.RegularExpressions;
using Optimus.Core.Domain.Personality;

namespace Optimus.Core.Personality;

/// <summary>Circonstances du moment, qui restreignent ce qu'il convient de dire.</summary>
/// <param name="CombatActive">
/// En combat, on abrège et on cesse de plaisanter. Un copilote bavard sous le feu est
/// insupportable — c'est la règle comportementale la plus utile du modèle.
/// </param>
/// <param name="MaxWordsOverride">Budget de mots imposé par une règle, prioritaire sur la verbosité.</param>
/// <param name="SuppressHumor">
/// Vrai quand la situation ne s'y prête pas.
///
/// Formulé en négatif à dessein : sur une structure, <c>default</c> met tous les champs à zéro
/// sans passer par les valeurs par défaut du constructeur. Un <c>AllowHumor = true</c> valait
/// donc <c>false</c> partout où le contexte n'était pas fourni — et l'humour se trouvait
/// désactivé en silence dans tout le produit. Le cas normal doit être celui que zéro exprime.
/// </param>
public readonly record struct ResponseContext(
    bool CombatActive = false,
    int? MaxWordsOverride = null,
    bool SuppressHumor = false)
{
    /// <summary>Traduit une décision du moteur de règles en contexte de composition.</summary>
    public static ResponseContext From(EffectiveBehavior behavior, bool combatActive = false)
    {
        ArgumentNullException.ThrowIfNull(behavior);
        return new ResponseContext(combatActive, behavior.MaxWords, !behavior.AllowHumor);
    }
}

/// <summary>Réplique choisie, avec de quoi expliquer pourquoi elle l'a été.</summary>
/// <param name="Text">Texte final, variables interpolées et lexique appliqué.</param>
/// <param name="SourceVariant">Variante d'origine, avant composition.</param>
/// <param name="CandidateCount">Nombre de variantes éligibles au moment du tirage.</param>
public sealed record ComposedResponse(string Text, string SourceVariant, int CandidateCount);

/// <summary>
/// Choisit et met en forme ce que dit le copilote.
///
/// L'algorithme complet est décrit dans docs/08 : éligibilité selon les traits, restriction
/// par le contexte, <b>anti-répétition</b>, tirage pondéré, puis composition — variables,
/// lexique, phrases interdites, budget de mots.
///
/// Rien n'y appelle le réseau. Le LLM, s'il est un jour activé, ne fera qu'ajouter une variante
/// supplémentaire à l'étape d'éligibilité, et subira les mêmes filtres que les autres — en
/// particulier celui des phrases interdites.
/// </summary>
public sealed partial class ResponseComposer
{
    /// <summary>Nombre de dernières variantes écartées pour éviter les répétitions.</summary>
    private const int RecentMemory = 3;

    private readonly Domain.Personality.Personality _personality;
    private readonly ResponseSet _responses;
    private readonly Dictionary<string, Queue<string>> _recent = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random;

    /// <param name="seed">
    /// Graine du tirage. Fixée, elle rend les réponses reproductibles : indispensable pour
    /// tester un composant dont le rôle est justement de varier.
    /// </param>
    public ResponseComposer(
        Domain.Personality.Personality personality,
        ResponseSet responses,
        int? seed = null)
    {
        _personality = personality ?? throw new ArgumentNullException(nameof(personality));
        _responses = responses ?? throw new ArgumentNullException(nameof(responses));
        _random = seed is int value ? new Random(value) : new Random();
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex VariablePattern();

    /// <summary>
    /// Compose une réplique. Retourne <c>null</c> si rien n'est prévu pour cette clé — le
    /// silence vaut mieux qu'un texte générique qui sonnerait faux.
    /// </summary>
    public ComposedResponse? Compose(
        string key,
        ResponseEvent responseEvent,
        IReadOnlyDictionary<string, string>? variables = null,
        ResponseContext context = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        List<ResponseVariant> candidates = Eligible(key, responseEvent, context);

        if (candidates.Count == 0)
        {
            return null;
        }

        List<ResponseVariant> fresh = WithoutRecentlyUsed(key, candidates);
        ResponseVariant chosen = PickWeighted(fresh.Count > 0 ? fresh : candidates);

        RememberUse(key, chosen.Text);

        string text = Interpolate(chosen.Text, variables);
        text = ApplyLexicon(text);
        text = TrimToWordBudget(text, context.MaxWordsOverride);

        return new ComposedResponse(text, chosen.Text, candidates.Count);
    }

    /// <summary>
    /// Compose la première clé qui donne quelque chose.
    ///
    /// Les clés arrivent de la plus spécifique à la plus générale : « Portes ouvertes,
    /// commandant » si la commande a sa propre réplique, « Reçu » sinon. Écrire une entrée
    /// dédiée devient ainsi facultatif — on ne le fait que là où ça vaut le détour.
    /// </summary>
    public ComposedResponse? ComposeFirst(
        IEnumerable<string> keys,
        ResponseEvent responseEvent,
        IReadOnlyDictionary<string, string>? variables = null,
        ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        foreach (string key in keys)
        {
            ComposedResponse? composed = Compose(key, responseEvent, variables, context);
            if (composed is not null)
            {
                return composed;
            }
        }

        return null;
    }

    /// <summary>Variantes retenues après filtrage par les traits puis par le contexte.</summary>
    private List<ResponseVariant> Eligible(string key, ResponseEvent responseEvent, ResponseContext context)
    {
        List<ResponseVariant> eligible = new();

        foreach (ResponseVariant variant in _responses.Variants(key, responseEvent))
        {
            if (variant.Requires is not null && !variant.Requires.IsSatisfiedBy(_personality))
            {
                continue;
            }

            if (ContainsForbiddenPhrase(variant.Text))
            {
                continue;
            }

            // En combat comme après un échec, l'humour est déplacé : on écarte les variantes
            // qui en dépendent, quelles que soient les valeurs des curseurs.
            bool humorous = variant.Requires?.HumorMin is > 0 || variant.Requires?.SarcasmMin is > 0;
            if (humorous && (context.SuppressHumor || context.CombatActive || responseEvent == ResponseEvent.Fail))
            {
                continue;
            }

            // Sous contrainte de brièveté, on préfère écarter d'emblée les variantes trop
            // longues plutôt que de les tronquer : une phrase amputée sonne plus faux qu'une
            // phrase courte choisie exprès.
            int budget = context.MaxWordsOverride ?? (context.CombatActive ? 8 : int.MaxValue);
            if (CountWords(variant.Text) > budget)
            {
                continue;
            }

            eligible.Add(variant);
        }

        return eligible;
    }

    private List<ResponseVariant> WithoutRecentlyUsed(string key, List<ResponseVariant> candidates)
    {
        if (!_recent.TryGetValue(key, out Queue<string>? used) || used.Count == 0)
        {
            return candidates;
        }

        return candidates.Where(c => !used.Contains(c.Text, StringComparer.Ordinal)).ToList();
    }

    private void RememberUse(string key, string text)
    {
        if (!_recent.TryGetValue(key, out Queue<string>? used))
        {
            used = new Queue<string>();
            _recent[key] = used;
        }

        used.Enqueue(text);

        while (used.Count > RecentMemory)
        {
            used.Dequeue();
        }
    }

    private ResponseVariant PickWeighted(List<ResponseVariant> candidates)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        double total = candidates.Sum(c => Math.Max(0.01, c.Weight));
        double roll = _random.NextDouble() * total;

        foreach (ResponseVariant candidate in candidates)
        {
            roll -= Math.Max(0.01, candidate.Weight);
            if (roll <= 0)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private string Interpolate(string text, IReadOnlyDictionary<string, string>? variables)
    {
        string withAddress = text;

        // {pilote} tire au sort une forme d'adresse : « commandant » n'est pas la seule façon
        // de s'adresser à quelqu'un, et l'alterner participe du naturel.
        if (withAddress.Contains("{pilote}", StringComparison.Ordinal) && _personality.Lexicon.AddressUser.Count > 0)
        {
            string address = _personality.Lexicon.AddressUser[_random.Next(_personality.Lexicon.AddressUser.Count)];
            withAddress = withAddress.Replace("{pilote}", address, StringComparison.Ordinal);
        }

        if (variables is null || variables.Count == 0)
        {
            return VariablePattern().Replace(withAddress, string.Empty).Trim();
        }

        return VariablePattern()
            .Replace(withAddress, match => variables.TryGetValue(match.Groups[1].Value, out string? value)
                ? value
                : string.Empty)
            .Trim();
    }

    private string ApplyLexicon(string text)
    {
        string result = text;

        foreach (KeyValuePair<string, string> replacement in _personality.Lexicon.Replacements)
        {
            result = Regex.Replace(
                result,
                $@"\b{Regex.Escape(replacement.Key)}\b",
                replacement.Value,
                RegexOptions.IgnoreCase);
        }

        return CollapseSpaces(result);
    }

    /// <summary>
    /// Tronque à la limite de mots dictée par la verbosité, en coupant à une frontière de
    /// phrase quand c'est possible : mieux vaut une phrase de moins qu'une phrase amputée.
    /// </summary>
    private string TrimToWordBudget(string text, int? overrideBudget = null)
    {
        int budget = overrideBudget ?? _personality.Traits.MaxWords;
        if (CountWords(text) <= budget)
        {
            return text;
        }

        string[] sentences = text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        System.Text.StringBuilder kept = new();

        foreach (string sentence in sentences)
        {
            string trimmed = sentence.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (CountWords(kept.ToString()) + CountWords(trimmed) > budget && kept.Length > 0)
            {
                break;
            }

            kept.Append(trimmed).Append(". ");
        }

        return kept.Length > 0 ? kept.ToString().Trim() : text;
    }

    private bool ContainsForbiddenPhrase(string text)
    {
        foreach (string forbidden in _personality.Lexicon.ForbiddenPhrases)
        {
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountWords(string text) =>
        text.Split([' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static string CollapseSpaces(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();
}
