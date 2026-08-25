using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Intent;

/// <summary>Façon dont une correspondance a été obtenue.</summary>
public enum MatchKind
{
    /// <summary>Phrase identique après normalisation.</summary>
    Exact,

    /// <summary>La phrase de référence est contenue dans l'énoncé, ou l'inverse.</summary>
    Contained,

    /// <summary>Correspondance approchée, tolérant les erreurs de transcription.</summary>
    Fuzzy,
}

/// <summary>Ce que le résolveur décide de faire de l'énoncé.</summary>
public enum IntentDecision
{
    /// <summary>Score élevé et sans rival proche : on exécute.</summary>
    Execute,

    /// <summary>Plusieurs candidats se tiennent : demander laquelle.</summary>
    Disambiguate,

    /// <summary>Score moyen : proposer et attendre confirmation.</summary>
    Confirm,

    /// <summary>Rien de crédible : escalade vers le LLM si activé, sinon aveu d'échec.</summary>
    Unknown,
}

/// <summary>Un candidat et son score.</summary>
public sealed record IntentCandidate(
    CommandDefinition Command,
    double Score,
    MatchKind Kind,
    string MatchedPhrase,
    CommandPolarity Polarity = CommandPolarity.Neutral)
{
    public override string ToString() =>
        $"{Command.Id} {Score:F2} ({Kind}, « {MatchedPhrase} »)" +
        (Polarity == CommandPolarity.Neutral ? string.Empty : $" [{Polarity}]");
}

/// <summary>Résultat complet d'une résolution, tel qu'affiché par le mode debug.</summary>
public sealed record IntentResolution(
    string RawText,
    string NormalizedText,
    IntentDecision Decision,
    IntentCandidate? Best,
    IReadOnlyList<IntentCandidate> Candidates)
{
    public bool HasMatch => Best is not null;
}

/// <summary>
/// Résolution locale, déterministe, sans réseau ni modèle de langue.
///
/// C'est la première ligne du pipeline de docs/07 : les commandes connues doivent se résoudre
/// ici, en quelques millisecondes. Le LLM n'intervient qu'en cas d'échec, ce qui garantit à la
/// fois le fonctionnement hors ligne, le coût nul et un comportement reproductible — trois
/// propriétés qu'un modèle de langue ne peut pas offrir.
/// </summary>
public sealed class FastIntentMatcher
{
    /// <summary>Au-dessus : exécution directe, à condition que le second candidat soit distancé.</summary>
    public const double ExecuteThreshold = 0.85;

    /// <summary>
    /// Au-dessus : on propose, on n'exécute pas.
    ///
    /// Calé à 0,70 après essai sur le moteur réel : à 0,55, une phrase sans rapport comme
    /// « fais un café » atteignait 0,63 et déclenchait une demande de confirmation absurde.
    /// Mieux vaut avouer ne pas comprendre — quitte à escalader vers le LLM — que proposer
    /// une commande que l'utilisateur n'a pas demandée.
    /// </summary>
    public const double ConfirmThreshold = 0.70;

    /// <summary>Écart minimal avec le second candidat pour trancher sans demander.</summary>
    public const double AmbiguityMargin = 0.15;

    private readonly List<(string Phrase, string[] Tokens, CommandDefinition Command, CommandPolarity Polarity)> _index = new();

    public FastIntentMatcher(CommandCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        foreach (CommandDefinition command in catalog.Commands)
        {
            Index(command, command.VoicePhrases, CommandPolarity.Neutral);
            Index(command, command.PhrasesOn, CommandPolarity.On);
            Index(command, command.PhrasesOff, CommandPolarity.Off);
        }
    }

    private void Index(CommandDefinition command, IReadOnlyList<string> phrases, CommandPolarity polarity)
    {
        foreach (string phrase in phrases)
        {
            string normalized = TextNormalizer.Normalize(phrase);
            if (normalized.Length == 0)
            {
                continue;
            }

            _index.Add((normalized, normalized.Split(' '), command, polarity));
        }
    }

    /// <summary>Nombre de phrases indexées.</summary>
    public int PhraseCount => _index.Count;

    /// <summary>
    /// Résout un énoncé. <paramref name="wakeWord"/> est retiré s'il figure en tête.
    /// </summary>
    public IntentResolution Resolve(string rawText, string? wakeWord = null)
    {
        string normalized = TextNormalizer.Normalize(rawText);
        if (!string.IsNullOrEmpty(wakeWord))
        {
            normalized = TextNormalizer.StripWakeWord(normalized, wakeWord);
        }

        if (normalized.Length == 0)
        {
            return new IntentResolution(rawText, normalized, IntentDecision.Unknown, null, Array.Empty<IntentCandidate>());
        }

        string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Un seul meilleur score est retenu par commande : dix phrases pour la même commande
        // ne doivent pas la faire paraître dix fois plus probable.
        Dictionary<string, IntentCandidate> bestPerCommand = new(StringComparer.Ordinal);

        foreach ((string phrase, string[] phraseTokens, CommandDefinition command, CommandPolarity polarity) in _index)
        {
            (double score, MatchKind kind) = Score(normalized, tokens, phrase, phraseTokens);
            if (score <= 0)
            {
                continue;
            }

            if (!bestPerCommand.TryGetValue(command.Id, out IntentCandidate? existing) || score > existing.Score)
            {
                bestPerCommand[command.Id] = new IntentCandidate(command, score, kind, phrase, polarity);
            }
        }

        List<IntentCandidate> candidates = bestPerCommand.Values
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Command.Id, StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return new IntentResolution(rawText, normalized, IntentDecision.Unknown, null, candidates);
        }

        IntentCandidate best = candidates[0];
        double runnerUp = candidates.Count > 1 ? candidates[1].Score : 0;
        IntentDecision decision;

        if (best.Score >= ExecuteThreshold)
        {
            // Une correspondance exacte n'est pas ambigue : l'enonce figure litteralement dans le
            // catalogue, associe a cette commande. « mode scan » et « mode scm » se ressemblent
            // assez pour que le second sorte a 0,91 - la marge d'ecart bloquait alors une phrase
            // pourtant reconnue mot pour mot, et « mode scan » ne s'est jamais executee.
            //
            // Le doute ne subsiste que si DEUX commandes revendiquent le meme enonce exact : c'est
            // un defaut du catalogue, et il doit se voir plutot que se resoudre au hasard.
            bool exactAndAlone = best.Kind == MatchKind.Exact
                && (candidates.Count < 2 || candidates[1].Kind != MatchKind.Exact);

            decision = exactAndAlone || best.Score - runnerUp >= AmbiguityMargin
                ? IntentDecision.Execute
                : IntentDecision.Disambiguate;
        }
        else if (best.Score >= ConfirmThreshold)
        {
            decision = IntentDecision.Confirm;
        }
        else
        {
            decision = IntentDecision.Unknown;
        }

        return new IntentResolution(rawText, normalized, decision, best, candidates);
    }

    private static (double Score, MatchKind Kind) Score(
        string utterance, string[] utteranceTokens, string phrase, string[] phraseTokens)
    {
        if (string.Equals(utterance, phrase, StringComparison.Ordinal))
        {
            return (1.0, MatchKind.Exact);
        }

        // Phrase de référence entièrement présente dans l'énoncé : « ouvre les portes
        // maintenant » contient « ouvre les portes ».
        //
        // La part de l'énoncé réellement couverte gouverne le score, et il faut qu'elle le
        // gouverne pour de bon. Le plancher valait 0,90 auparavant — au-dessus du seuil
        // d'exécution — de sorte qu'un mot isolé revendiquait n'importe quelle phrase qui le
        // contenait : « priorité aux armes » déclenchait la bascule des armes à 0,93. Un
        // vocable d'un mot dans un énoncé de trois tombe désormais dans la bande de
        // proposition, où Optimus demande confirmation au lieu d'agir.
        if (ContainsSequence(utteranceTokens, phraseTokens))
        {
            double coverage = (double)phraseTokens.Length / utteranceTokens.Length;
            return (Math.Round(0.72 + (0.26 * coverage), 4), MatchKind.Contained);
        }

        double tokenScore = TokenSetSimilarity(utteranceTokens, phraseTokens);
        double editScore = EditSimilarity(utterance, phrase);

        // Les deux mesures se complètent : la première encaisse l'ordre des mots et les
        // omissions, la seconde les fautes à l'intérieur d'un mot — « bouquillés » pour
        // « boucliers », cas réellement observé au spike S0-2.
        double combined = (tokenScore * 0.6) + (editScore * 0.4);

        return combined >= 0.45 ? (Math.Round(combined, 4), MatchKind.Fuzzy) : (0, MatchKind.Fuzzy);
    }

    private static bool ContainsSequence(string[] haystack, string[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (int start = 0; start <= haystack.Length - needle.Length; start++)
        {
            bool match = true;
            for (int k = 0; k < needle.Length; k++)
            {
                if (!string.Equals(haystack[start + k], needle[k], StringComparison.Ordinal))
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Proportion de mots communs, en tolérant une faute par mot.</summary>
    private static double TokenSetSimilarity(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        int matches = 0;
        bool[] used = new bool[b.Length];

        foreach (string word in a)
        {
            for (int j = 0; j < b.Length; j++)
            {
                if (used[j])
                {
                    continue;
                }

                if (string.Equals(word, b[j], StringComparison.Ordinal) || IsNearWord(word, b[j]))
                {
                    used[j] = true;
                    matches++;
                    break;
                }
            }
        }

        return (double)matches / Math.Max(a.Length, b.Length);
    }

    private static bool IsNearWord(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 3)
        {
            return false;
        }

        int tolerance = Math.Max(a.Length, b.Length) >= 8 ? 3 : 2;
        return TextNormalizer.LevenshteinDistance(a, b) <= tolerance;
    }

    private static double EditSimilarity(string a, string b)
    {
        int longest = Math.Max(a.Length, b.Length);
        if (longest == 0)
        {
            return 0;
        }

        int distance = TextNormalizer.LevenshteinDistance(a, b);
        return 1.0 - ((double)distance / longest);
    }
}
