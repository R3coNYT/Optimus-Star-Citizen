using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Profiles;
using Optimus.Core.Intent;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// Construction de la grammaire d'écoute.
///
/// C'est ici que se joue la sûreté de l'écoute permanente : ce qui n'entre pas dans la grammaire
/// ne peut pas être reconnu. Ces tests vérifient donc moins une fonctionnalité qu'une garantie.
/// </summary>
public sealed class GrammarTests
{
    private static CommandCatalog LoadCatalog() =>
        JsonCatalogLoader.LoadCatalog(
            Path.Combine(TestData.RepositoryRoot, "data", "commands", "starcitizen.core.json")).Value;

    [Fact]
    public void En_ecoute_permanente_toute_alternative_commence_par_le_mot_d_eveil()
    {
        // La garantie centrale : sans « Optimus » en tete, rien ne peut etre reconnu, donc une
        // conversation ordinaire ne declenche aucune commande.
        CommandCatalog catalog = LoadCatalog();
        VoiceInputSettings settings = new(ListeningMode.AlwaysOn);

        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, "Optimus", settings);

        Assert.True(grammar.WakeWordRequired);
        Assert.NotEmpty(grammar.Alternatives);

        // Les alternatives sont ACCENTUEES depuis que le moteur en derive la prononciation :
        // « Optimus prépare le décollage », pas « optimus prepare le decollage ». La garantie
        // porte sur le sens, pas sur la casse - on normalise avant de la verifier.
        Assert.All(grammar.Alternatives, phrase =>
            Assert.StartsWith("optimus ", TextNormalizer.Normalize(phrase), StringComparison.Ordinal));
    }

    [Fact]
    public void En_push_to_talk_le_mot_d_eveil_devient_facultatif()
    {
        CommandCatalog catalog = LoadCatalog();
        VoiceInputSettings settings = new(ListeningMode.PushToTalk);

        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, "Optimus", settings);

        Assert.False(grammar.WakeWordRequired);

        string[] normalized = grammar.Alternatives.Select(TextNormalizer.Normalize).ToArray();
        Assert.Contains("ouvre les portes", normalized);
        Assert.Contains("optimus ouvre les portes", normalized);
    }

    [Fact]
    public void Le_push_to_talk_peut_exiger_le_mot_d_eveil_si_on_le_demande()
    {
        CommandCatalog catalog = LoadCatalog();
        VoiceInputSettings settings = new(ListeningMode.PushToTalk, RequireWakeWordInPushToTalk: true);

        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, "Optimus", settings);

        Assert.True(grammar.WakeWordRequired);
        Assert.DoesNotContain("ouvre les portes", grammar.Alternatives);
    }

    [Fact]
    public void Une_phrase_reconnue_designe_sa_commande()
    {
        CommandCatalog catalog = LoadCatalog();
        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, "Optimus", new VoiceInputSettings());

        Assert.Equal("ship.doors.toggle", grammar.Resolve("optimus ouvre les portes"));
        Assert.Equal("ship.lights.toggle", grammar.Resolve("Optimus, allume les lumières"));

        // La normalisation absorbe casse, accents et ponctuation, comme sur l'entree clavier.
        Assert.Equal("quantum.engage", grammar.Resolve("OPTIMUS LANCE LE QUANTUM"));
    }

    [Fact]
    public void Une_phrase_hors_grammaire_ne_designe_rien()
    {
        CommandCatalog catalog = LoadCatalog();
        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, "Optimus", new VoiceInputSettings());

        Assert.Null(grammar.Resolve("tu as vu le match hier soir"));
        Assert.Null(grammar.Resolve("ouvre les portes"));  // sans mot d'eveil, en ecoute permanente
        Assert.Null(grammar.Resolve(""));
    }

    [Fact]
    public void La_grammaire_couvre_toutes_les_commandes_du_catalogue()
    {
        CommandCatalog catalog = LoadCatalog();
        VoiceGrammar grammar = VoiceGrammarBuilder.Build(catalog, "Optimus", new VoiceInputSettings());

        HashSet<string> covered = new(
            grammar.PhraseToCommand.Values.Select(t => t.CommandId), StringComparer.Ordinal);

        List<string> missing = catalog.Commands
            .Where(c => !covered.Contains(c.Id))
            .Select(c => c.Id)
            .ToList();

        Assert.True(missing.Count == 0, "commandes inatteignables à la voix : " + string.Join(", ", missing));
    }

    [Fact]
    public void Le_profil_du_depot_demande_bien_l_ecoute_permanente()
    {
        LoadResult<UserProfile> profile = ProfileLoader.Load(
            Path.Combine(TestData.RepositoryRoot, "data", "profiles", "default.json"));

        Assert.Empty(profile.Issues);
        Assert.Equal(ListeningMode.AlwaysOn, profile.Value.VoiceInput.Mode);
        Assert.Equal("INSERT", profile.Value.VoiceInput.PushToTalkKey);
        Assert.True(profile.Value.VoiceInput.WakeWordRequired);
        Assert.Equal(0.65, profile.Value.VoiceInput.ConfidenceThreshold, 3);
        Assert.Equal(0.35, profile.Value.VoiceInput.NoiseFloor, 3);
    }

    [Fact]
    public void Un_mode_inconnu_retombe_sur_l_ecoute_permanente_en_le_signalant()
    {
        string path = Path.Combine(Path.GetTempPath(), $"optimus-profil-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "id": "test", "voice_input": { "mode": "telepathie" } }""");

        try
        {
            LoadResult<UserProfile> profile = ProfileLoader.Load(path);

            Assert.NotEmpty(profile.Issues);
            Assert.Equal(ListeningMode.AlwaysOn, profile.Value.VoiceInput.Mode);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
