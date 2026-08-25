using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Optimus.Core.Intent;

/// <summary>
/// Ramène une transcription et une phrase de référence à une forme comparable.
///
/// Le spike S0-2 a montré ce que le moteur doit absorber : Whisper capitalise, ponctue et
/// ajoute des points finaux (« Optimus Rapport Système. »), et il achoppe sur le vocabulaire
/// du domaine. La normalisation traite le premier problème ; le matcher flou et le lexique
/// s'occupent du second.
/// </summary>
public static partial class TextNormalizer
{
    /// <summary>
    /// Mots parasites d'une commande vocale : politesses, hésitations, interjections.
    /// Retirés avant comparaison pour que « ouvre-moi les portes s'il te plaît » et
    /// « ouvre les portes » convergent.
    /// </summary>
    private static readonly HashSet<string> FillerWords = new(StringComparer.Ordinal)
    {
        "euh", "heu", "hum", "bah", "ben", "alors", "donc", "allez", "hop",
        "stp", "svp", "please",
        "s", "il", "te", "plait", "plait",
        "maintenant", "tout", "de", "suite",
    };

    /// <summary>
    /// Mots à ne jamais retirer, même s'ils figurent dans les parasites.
    /// « tout » est un parasite dans « vas-y tout de suite », mais porteur de sens ailleurs.
    /// </summary>
    private static readonly HashSet<string> ProtectedWords = new(StringComparer.Ordinal)
    {
        "de", "le", "la", "les", "l", "du", "des",
    };

    /// <summary>
    /// Nombres écrits en toutes lettres.
    ///
    /// « un » et « une » en sont volontairement absents : en français ce sont bien plus souvent
    /// des articles que des quantités. Les convertir transformait « fais un rapport » en
    /// « fais 1 rapport » et dégradait la comparaison au lieu de l'améliorer.
    /// </summary>
    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.Ordinal)
    {
        ["zero"] = "0", ["deux"] = "2", ["trois"] = "3",
        ["quatre"] = "4", ["cinq"] = "5", ["six"] = "6", ["sept"] = "7", ["huit"] = "8",
        ["neuf"] = "9", ["dix"] = "10",
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpaces();

    [GeneratedRegex(@"[^a-z0-9 ]")]
    private static partial Regex NonAlphanumeric();

    /// <summary>
    /// Normalise un texte : minuscules, accents supprimés, ponctuation retirée, élisions
    /// séparées, nombres en toutes lettres convertis, mots parasites écartés.
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string working = text.ToLowerInvariant();

        // Les élisions et traits d'union deviennent des séparations : « ouvre-moi » -> « ouvre moi ».
        working = working.Replace('\'', ' ').Replace('’', ' ').Replace('-', ' ');

        working = RemoveDiacritics(working);
        working = NonAlphanumeric().Replace(working, " ");
        working = MultipleSpaces().Replace(working, " ").Trim();

        if (working.Length == 0)
        {
            return string.Empty;
        }

        List<string> kept = new();
        foreach (string word in working.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string token = NumberWords.TryGetValue(word, out string? digit) ? digit : word;

            if (FillerWords.Contains(token) && !ProtectedWords.Contains(token))
            {
                continue;
            }

            kept.Add(token);
        }

        // Une phrase composée uniquement de parasites doit rester quelque chose plutôt que rien :
        // mieux vaut tenter une résolution que renvoyer un vide inexplicable à l'utilisateur.
        return kept.Count > 0 ? string.Join(' ', kept) : working;
    }

    /// <summary>Découpe un texte normalisé en mots.</summary>
    public static string[] Tokenize(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Retire le mot d'éveil en tête d'énoncé, s'il est présent.
    ///
    /// La comparaison est tolérante : le spike S0-2 a certes reconnu « Optimus » sur les
    /// 48 mesures, mais un simple « ok optimus » ou une finale mangée ne doit pas empêcher
    /// la commande de passer.
    /// </summary>
    public static string StripWakeWord(string normalizedText, string wakeWord)
    {
        if (string.IsNullOrEmpty(normalizedText) || string.IsNullOrEmpty(wakeWord))
        {
            return normalizedText;
        }

        string wake = Normalize(wakeWord);
        string[] words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return normalizedText;
        }

        int skip = 0;

        // « ok optimus », « hey optimus »
        if (words.Length > 1 && (words[0] == "ok" || words[0] == "hey" || words[0] == "eh"))
        {
            skip = 1;
        }

        if (skip < words.Length && IsCloseEnough(words[skip], wake))
        {
            skip++;
            return string.Join(' ', words.Skip(skip));
        }

        return normalizedText;
    }

    /// <summary>
    /// Le mot entendu est-il assez proche du mot d'éveil.
    ///
    /// La tolérance est délibérément serrée : une seule modification jusqu'à neuf lettres.
    /// À deux, « optique » passait pour « optimus » — un mot courant, sans rapport, qui aurait
    /// fait croire au copilote qu'on l'appelait. Une lettre suffit à absorber les vraies
    /// approximations de transcription (« optimuss », « optimis ») sans ouvrir la porte au
    /// voisinage lexical.
    /// </summary>
    private static bool IsCloseEnough(string candidate, string reference)
    {
        if (candidate == reference)
        {
            return true;
        }

        int tolerance = reference.Length >= 10 ? 2 : 1;
        return LevenshteinDistance(candidate, reference) <= tolerance;
    }

    /// <summary>Distance de Levenshtein, utilisée pour la tolérance aux fautes de transcription.</summary>
    public static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static string RemoveDiacritics(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
