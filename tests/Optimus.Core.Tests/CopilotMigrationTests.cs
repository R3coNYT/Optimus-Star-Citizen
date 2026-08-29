using System.Text.Json;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Loading;
using Optimus.Core.Localization;

namespace Optimus.Core.Tests;

/// <summary>
/// Une fiche de copilote écrite par une version antérieure doit rester lisible.
///
/// Le cas n'est pas théorique : l'installateur préserve la fiche du pilote — elle porte sa
/// voix, son mot d'éveil et ses curseurs — et ne la remplace jamais. Un champ retiré du
/// fichier livré ne disparaît donc pas des machines.
///
/// Mesuré le 2026-08-29 sur le poste de jeu : « responses_ref: responses.fr.json », hérité
/// d'avant la traduction, épinglait les répliques françaises quelle que soit la langue. Le
/// catalogue passait à l'anglais, Optimus reconnaissait « open the doors », et répondait
/// « Sas ouverts ».
/// </summary>
public sealed class CopilotMigrationTests : IDisposable
{
    private readonly string _directory;

    public CopilotMigrationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "optimus-copilot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        // Les vraies répliques livrées, et non des factices : ce qu'on vérifie ici est
        // précisément que le bon fichier est choisi parmi ceux qui existent.
        string shipped = Path.Combine(TestData.RepositoryRoot, "data", "copilots", "optimus");

        foreach (string name in new[] { "responses.fr.json", "responses.en.json", "personality.json" })
        {
            File.Copy(Path.Combine(shipped, name), Path.Combine(_directory, name));
        }
    }

    /// <summary>Écrit une fiche telle qu'une version antérieure l'aurait laissée.</summary>
    private void WriteManifest(string language, string? responsesRef)
    {
        string pin = responsesRef is null ? string.Empty : $",\n  \"responses_ref\": \"{responsesRef}\"";

        File.WriteAllText(
            Path.Combine(_directory, "copilot.json"),
            $$"""
              {
                "id": "optimus",
                "name": "Optimus",
                "language": "{{language}}",
                "wake_word": "Optimus",
                "personality_ref": "personality.json"{{pin}}
              }
              """);
    }

    [Fact]
    public void Un_epinglage_herite_ne_verrouille_plus_la_langue()
    {
        WriteManifest("fr-FR", "responses.fr.json");

        Copilot copilot = CopilotLoader.Load(_directory, Language.English).Value;

        Assert.Equal(Language.English, copilot.Language);

        // La preuve par le fichier lui-même : chaque jeu de répliques déclare sa langue.
        Assert.Equal(Language.English, copilot.Responses.Locale);
    }

    [Fact]
    public void Un_nom_de_fichier_vraiment_choisi_garde_son_passe_droit()
    {
        // Un copilote qui tient à son propre fichier doit continuer d'être servi : la
        // correction ne doit pas confisquer la fonction, seulement son usage par défaut.
        File.Copy(
            Path.Combine(_directory, "responses.fr.json"),
            Path.Combine(_directory, "les-repliques.json"));

        WriteManifest("en-US", "les-repliques.json");

        Copilot copilot = CopilotLoader.Load(_directory, Language.English).Value;

        Assert.Equal(Language.French, copilot.Responses.Locale);
    }

    [Fact]
    public void Sans_epinglage_la_langue_decide()
    {
        WriteManifest("fr-FR", responsesRef: null);

        Assert.Equal(
            Language.English,
            CopilotLoader.Load(_directory, Language.English).Value.Language);

        // Et la fiche continue de décider quand personne ne demande de langue.
        Assert.Equal(
            Language.French,
            CopilotLoader.Load(_directory).Value.Language);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Un dossier temporaire qui survit n'est pas un echec d'essai.
        }
    }
}
