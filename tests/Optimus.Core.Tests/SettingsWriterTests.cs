using System.Text.Json;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Domain.Profiles;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// L'ecriture des reglages doit PATCHER les fichiers, jamais les regenerer : ils contiennent
/// bien plus que ce que l'interface expose, et sacrifier le reste se ferait en silence.
/// </summary>
public sealed class SettingsWriterTests : IDisposable
{
    private readonly string _directory;

    public SettingsWriterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"optimus-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Ecrire_les_reglages_d_ecoute_preserve_le_reste_du_profil()
    {
        string path = Copy(Path.Combine("data", "profiles", "default.json"), "profile.json");

        SettingsWriter.SaveVoiceInput(path, new VoiceInputSettings(
            ListeningMode.PushToTalk, "F13", RequireWakeWordInPushToTalk: true,
            ConfidenceThreshold: 0.72, NoiseFloor: 0.28));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        JsonElement voice = root.GetProperty("voice_input");

        Assert.Equal("push_to_talk", voice.GetProperty("mode").GetString());
        Assert.Equal("F13", voice.GetProperty("push_to_talk_key").GetString());
        Assert.True(voice.GetProperty("require_wake_word_in_push_to_talk").GetBoolean());
        Assert.Equal(0.72, voice.GetProperty("confidence_threshold").GetDouble(), 3);

        // Les notes expliquent POURQUOI ces reglages sont ce qu'ils sont. Les perdre a la
        // premiere sauvegarde depuis l'interface serait une regression invisible.
        Assert.True(voice.TryGetProperty("notes", out JsonElement notes));
        Assert.True(notes.GetArrayLength() > 0);

        // Les sections que l'interface n'expose pas doivent survivre telles quelles.
        Assert.True(root.TryGetProperty("hotkeys", out _));
        Assert.True(root.TryGetProperty("safety", out _));
        Assert.Equal("optimus", root.GetProperty("preferred_copilot").GetString());

        // Et le resultat doit se recharger.
        LoadResult<UserProfile> reloaded = ProfileLoader.Load(path);
        Assert.Empty(reloaded.Issues);
        Assert.Equal(ListeningMode.PushToTalk, reloaded.Value.VoiceInput.Mode);
        Assert.Equal("F13", reloaded.Value.VoiceInput.PushToTalkKey);
    }

    [Fact]
    public void Ecrire_les_curseurs_preserve_le_lexique_et_les_regles()
    {
        string path = Copy(Path.Combine("data", "copilots", "optimus", "personality.json"), "personality.json");

        SettingsWriter.SaveTraits(path, new PersonalityTraits(
            Humor: 70, Sarcasm: 60, Formality: 20, Verbosity: 90));

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal(70, root.GetProperty("traits").GetProperty("humor").GetInt32());
        Assert.Equal(20, root.GetProperty("traits").GetProperty("formality").GetInt32());

        // Le lexique et les regles ne sont pas modifiables depuis l'interface : ils doivent
        // donc etre exactement intacts.
        Assert.True(root.TryGetProperty("lexicon", out JsonElement lexicon));
        Assert.True(lexicon.GetProperty("forbidden_phrases").GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("rules", out JsonElement rules));
        Assert.True(rules.GetArrayLength() >= 4);
        Assert.True(root.TryGetProperty("style", out _));
    }

    [Fact]
    public void Ecrire_la_voix_preserve_les_references_du_copilote()
    {
        string path = Copy(Path.Combine("data", "copilots", "optimus", "copilot.json"), "copilot.json");

        SettingsWriter.SaveCopilotVoice(
            path, new VoiceConfig("windows-onecore", "Microsoft Hortense", 1.2, 0.6), "Jarvis");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        Assert.Equal("Jarvis", root.GetProperty("wake_word").GetString());
        Assert.Equal("Microsoft Hortense", root.GetProperty("voice").GetProperty("voice_id").GetString());
        Assert.Equal(1.2, root.GetProperty("voice").GetProperty("rate").GetDouble(), 3);

        // Sans cette reference, le copilote se chargerait sans personnalite - et sans la
        // moindre erreur, ce qui est le pire des symptomes.
        Assert.Equal("personality.json", root.GetProperty("personality_ref").GetString());
        Assert.Equal("windows-onecore", root.GetProperty("voice").GetProperty("provider").GetString());

        // « responses_ref » a disparu du copilote livre : le fichier de repliques se deduit
        // desormais de la langue. L'ecriture ne doit pas en inventer un, sans quoi elle
        // figerait le francais et le choix de langue n'aurait plus aucun effet.
        Assert.False(root.TryGetProperty("responses_ref", out _));
    }

    [Fact]
    public void Ecrire_la_voix_preserve_une_reference_de_repliques_explicite()
    {
        // Un copilote peut tenir a nommer son fichier autrement - c'est le passe-droit que
        // « responses_ref » conserve. L'ecriture ne doit pas l'effacer au passage.
        string path = Copy(Path.Combine("data", "copilots", "optimus", "copilot.json"), "copilot.json");

        string source = File.ReadAllText(path);
        File.WriteAllText(path, source.Replace(
            "\"personality_ref\": \"personality.json\"",
            "\"personality_ref\": \"personality.json\", \"responses_ref\": \"repliques.json\""));

        SettingsWriter.SaveCopilotVoice(
            path, new VoiceConfig("windows-onecore", "Microsoft Hortense", 1.0, 0.9), "Optimus");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal("repliques.json", document.RootElement.GetProperty("responses_ref").GetString());
    }

    [Fact]
    public void Les_accents_ne_ressortent_pas_echappes()
    {
        string path = Copy(Path.Combine("data", "copilots", "optimus", "personality.json"), "personality.json");

        SettingsWriter.SaveTraits(path, new PersonalityTraits(Humor: 50));

        // Le fichier reste lisible et modifiable a la main : é partout le rendrait hostile.
        string text = File.ReadAllText(path);
        Assert.DoesNotContain(@"\u00", text, StringComparison.Ordinal);
    }

    private string Copy(string relative, string name)
    {
        string source = Path.Combine(TestData.RepositoryRoot, relative);
        string destination = Path.Combine(_directory, name);
        File.Copy(source, destination);
        return destination;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
