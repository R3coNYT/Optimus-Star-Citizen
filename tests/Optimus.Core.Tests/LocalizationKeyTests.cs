using System.Text.RegularExpressions;

namespace Optimus.Core.Tests;

/// <summary>
/// Toute clé employée par l'écran existe, dans les deux langues.
///
/// Une clé manquante ne casse rien : ni la compilation, ni l'exécution. Elle s'affiche telle
/// quelle, et ne se voit que si quelqu'un regarde le bon onglet au bon moment. C'est ainsi que
/// « Log.Heard » a été montré au pilote le 2026-08-29, à la place de ce qu'il venait de dire.
///
/// Un script le vérifiait déjà — <c>tools/check-strings.py</c> — mais il ne tournait que
/// lorsqu'on y pensait, et le défaut est passé entre deux exécutions. Voilà pourquoi c'est
/// devenu un essai : ce qui protège d'un oubli ne peut pas dépendre de la mémoire.
///
/// L'essai lit les FICHIERS du dépôt plutôt que l'assembly : les dictionnaires sont du XAML de
/// l'application WPF, et faire dépendre les essais de WPF pour lire deux listes de chaînes
/// coûterait bien plus que ce que cela rapporte.
/// </summary>
public sealed class LocalizationKeyTests
{
    private static string App => Path.Combine(TestData.RepositoryRoot, "src", "Optimus.App");

    private static string Dictionary(string language) =>
        Path.Combine(App, "Localization", $"Strings.{language}.xaml");

    /// <summary>Les clés déclarées dans un dictionnaire.</summary>
    private static HashSet<string> Declared(string language) =>
        Regex.Matches(File.ReadAllText(Dictionary(language)), "x:Key=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Les clés que le code demande.
    ///
    /// La recherche prend TOUT littéral pointé écrit à l'intérieur d'un appel à
    /// <c>Localizer.T(…)</c>, en suivant les parenthèses. Se contenter de la chaîne qui suit
    /// immédiatement la parenthèse laisserait passer les deux formes que le code emploie le
    /// plus : le ternaire, et l'expression <c>switch</c> qui choisit une clé parmi cinq.
    ///
    /// Le filtre « un point, des lettres » écarte les arguments de formatage — « F2 »,
    /// « dd/MM HH:mm » — sans avoir à distinguer la clé des valeurs par la position.
    /// </summary>
    private static HashSet<string> Used()
    {
        HashSet<string> used = new(StringComparer.Ordinal);
        Regex key = new(@"^[A-Za-z][A-Za-z0-9]*\.[A-Za-z][A-Za-z0-9.]*$");

        foreach (string file in Directory.EnumerateFiles(App, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string code = File.ReadAllText(file);

            foreach (Match call in Regex.Matches(code, @"Localizer\.T\("))
            {
                string arguments = Arguments(code, call.Index + call.Length);

                foreach (Match literal in Regex.Matches(arguments, "\"([^\"]*)\""))
                {
                    if (key.IsMatch(literal.Groups[1].Value))
                    {
                        used.Add(literal.Groups[1].Value);
                    }
                }
            }
        }

        return used;
    }

    /// <summary>Le texte entre la parenthèse ouvrante et celle qui la referme.</summary>
    private static string Arguments(string code, int start)
    {
        int depth = 1;

        for (int i = start; i < code.Length; i++)
        {
            if (code[i] == '(')
            {
                depth++;
            }
            else if (code[i] == ')')
            {
                depth--;

                if (depth == 0)
                {
                    return code[start..i];
                }
            }
        }

        return code[start..];
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void Chaque_cle_employee_est_declaree(string language)
    {
        string[] missing = Used().Except(Declared(language)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            missing.Length == 0,
            $"absentes de Strings.{language}.xaml : {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Les deux dictionnaires portent exactement les mêmes clés.
    ///
    /// Une clé présente d'un seul côté ne se voit que dans l'autre langue, donc jamais chez qui
    /// l'a écrite. C'est le défaut le plus discret de tous.
    /// </summary>
    [Fact]
    public void Les_deux_langues_declarent_les_memes_cles()
    {
        HashSet<string> french = Declared("fr");
        HashSet<string> english = Declared("en");

        Assert.True(
            french.SetEquals(english),
            $"seulement en français : {string.Join(", ", french.Except(english).Order(StringComparer.Ordinal))}"
            + $" · seulement en anglais : {string.Join(", ", english.Except(french).Order(StringComparer.Ordinal))}");
    }

    /// <summary>
    /// Autant de trous « {0} » d'un côté que de l'autre.
    ///
    /// Un « {1} » présent dans une seule langue lève une exception de formatage au moment de
    /// l'afficher — donc à l'exécution, dans une langue sur deux, et jamais à la compilation.
    /// </summary>
    [Fact]
    public void Les_deux_langues_attendent_le_meme_nombre_d_arguments()
    {
        string[] mismatched = Declared("fr")
            .Where(k => Placeholders("fr", k) != Placeholders("en", k))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            mismatched.Length == 0,
            $"nombre de trous différent : {string.Join(", ", mismatched)}");
    }

    private static int Placeholders(string language, string key)
    {
        Match entry = Regex.Match(
            File.ReadAllText(Dictionary(language)),
            $"x:Key=\"{Regex.Escape(key)}\"[^>]*>(.*?)</sys:String>",
            RegexOptions.Singleline);

        if (!entry.Success)
        {
            return -1;
        }

        MatchCollection holes = Regex.Matches(entry.Groups[1].Value, @"\{(\d+)\}");

        return holes.Count == 0 ? 0 : holes.Select(m => int.Parse(m.Groups[1].Value)).Max() + 1;
    }
}
