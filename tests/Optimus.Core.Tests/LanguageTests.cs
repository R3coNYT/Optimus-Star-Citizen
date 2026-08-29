using Optimus.Core.Localization;

namespace Optimus.Core.Tests;

/// <summary>
/// La langue : sa résolution, et le repli des fichiers.
///
/// Ce qui est verrouillé ici n'est pas la traduction — c'est la garantie qu'une langue
/// inconnue, un profil écrit à la main ou un fichier anglais manquant ramènent Optimus à
/// quelque chose qui fonctionne. Un écran muet est le pire des symptômes.
/// </summary>
public sealed class LanguageTests
{
    [Theory]
    [InlineData("fr-FR", Language.French)]
    [InlineData("en-US", Language.English)]
    [InlineData("fr", Language.French)]
    [InlineData("en", Language.English)]
    [InlineData("EN-GB", Language.English)]      // une graphie ne prive pas de sa langue
    [InlineData("en_US", Language.English)]      // un profil ecrit a la main
    [InlineData("de-DE", Language.French)]       // inconnue : repli
    [InlineData("", Language.French)]
    [InlineData(null, Language.French)]
    public void Une_langue_se_ramene_toujours_a_une_langue_connue(string? raw, string expected) =>
        Assert.Equal(expected, Language.Resolve(raw));

    [Fact]
    public void Le_nom_de_la_langue_est_ecrit_dans_cette_langue()
    {
        // « French » dans une liste francaise obligerait a connaitre l'anglais pour revenir.
        Assert.Equal("Français", Language.DisplayName(Language.French));
        Assert.Equal("English", Language.DisplayName(Language.English));
    }

    [Fact]
    public void Le_fichier_de_la_langue_demandee_est_choisi_quand_il_existe()
    {
        using Sandbox sandbox = new();
        sandbox.Write("responses.fr.json", "{}");
        sandbox.Write("responses.en.json", "{}");

        Assert.Equal(
            sandbox.Path("responses.en.json"),
            Language.Localized(sandbox.Root, "responses", ".json", Language.English));
    }

    [Fact]
    public void Un_fichier_manquant_retombe_sur_le_francais()
    {
        // Le cas qui arrivera vraiment : une traduction pas encore ecrite, ou un fichier que
        // le pilote a supprime. Optimus doit parler francais, pas se taire.
        using Sandbox sandbox = new();
        sandbox.Write("responses.fr.json", "{}");

        Assert.Equal(
            sandbox.Path("responses.fr.json"),
            Language.Localized(sandbox.Root, "responses", ".json", Language.English));
    }

    [Fact]
    public void Le_fichier_sans_suffixe_reste_le_dernier_recours()
    {
        // Le catalogue d'origine s'appelle « starcitizen.core.json », sans langue : il a ete
        // ecrit avant que la question se pose. Il doit continuer d'etre trouve.
        using Sandbox sandbox = new();
        sandbox.Write("starcitizen.core.json", "{}");

        Assert.Equal(
            sandbox.Path("starcitizen.core.json"),
            Language.Localized(sandbox.Root, "starcitizen.core", ".json", Language.English));
    }

    [Fact]
    public void Rien_du_tout_se_dit_null_plutot_que_de_rendre_un_chemin_faux()
    {
        using Sandbox sandbox = new();

        Assert.Null(Language.Localized(sandbox.Root, "responses", ".json", Language.French));
    }

    private sealed class Sandbox : IDisposable
    {
        public Sandbox() => Directory.CreateDirectory(Root);

        public string Root { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "optimus-lang-" + Guid.NewGuid().ToString("N"));

        public string Path(string name) => System.IO.Path.Combine(Root, name);

        public void Write(string name, string content) => File.WriteAllText(Path(name), content);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Un dossier temporaire qui survit ne vaut pas un test rouge.
            }
        }
    }
}
