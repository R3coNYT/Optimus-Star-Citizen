using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// Le reglage de la reconnaissance doit devenir cumulatif : ce que le pilote apprend a Optimus
/// lui appartient, survit aux mises a jour, et ne peut pas rendre une commande inatteignable.
/// </summary>
public sealed class UnderstandingTests : IDisposable
{
    private readonly string _directory;
    private readonly CommandCatalog _catalog;

    public UnderstandingTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"optimus-comprehension-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(TestData.RepositoryRoot, "data", "commands", "starcitizen.core.json")).Value;
    }

    [Fact]
    public void Les_occurrences_identiques_sont_comptees_et_non_empilees()
    {
        UnderstandingLog log = new();

        for (int i = 0; i < 30; i++)
        {
            log.Record("mode scan", HesitationKind.Proposed, "scan.mode.toggle", 0.55);
        }

        // Trente lignes identiques n'apprendraient rien de plus qu'une ligne comptee trente
        // fois, et noieraient le reste.
        Assert.Equal(1, log.Count);
        Assert.Equal(30, log.Entries[0].Count);
    }

    [Fact]
    public void Le_journal_se_relit_apres_ecriture()
    {
        string path = Path.Combine(_directory, "comprehension.json");

        UnderstandingLog log = new();
        log.Record("leurre", HesitationKind.Proposed, "combat.countermeasure.decoy", 0.42);
        log.Record("leurre", HesitationKind.Proposed, "combat.countermeasure.decoy", 0.48);
        log.Record("ping radar", HesitationKind.Denied, "scan.ping", 0.61);
        log.Save(path);

        UnderstandingLog reloaded = UnderstandingLog.Load(path);

        Assert.Equal(2, reloaded.Count);

        Hesitation top = reloaded.Entries[0];
        Assert.Equal("leurre", top.Heard);
        Assert.Equal(2, top.Count);
        Assert.Equal(0.48, top.LastConfidence, 3);
    }

    [Fact]
    public void Une_formulation_apprise_rejoint_la_commande_visee()
    {
        PhraseAlias alias = new(
            "combat.countermeasure.decoy", "balance les leurres",
            CommandPolarity.Neutral, DateTimeOffset.UtcNow);

        CommandCatalog enriched = UserPhrases.Apply(_catalog, [alias]);

        Assert.True(enriched.TryGet("combat.countermeasure.decoy", out CommandDefinition? command));
        Assert.Contains("balance les leurres", command!.VoicePhrases);

        // Le catalogue livre n'est pas touche : c'est lui qu'une mise a jour remplacera.
        Assert.True(_catalog.TryGet("combat.countermeasure.decoy", out CommandDefinition? original));
        Assert.DoesNotContain("balance les leurres", original!.VoicePhrases);
    }

    [Fact]
    public void Une_formulation_apprise_avec_un_sens_rejoint_la_bonne_liste()
    {
        PhraseAlias alias = new(
            "ship.lights.toggle", "balance la lumière", CommandPolarity.On, DateTimeOffset.UtcNow);

        CommandCatalog enriched = UserPhrases.Apply(_catalog, [alias]);

        Assert.True(enriched.TryGet("ship.lights.toggle", out CommandDefinition? command));
        Assert.Contains("balance la lumière", command!.PhrasesOn);
        Assert.DoesNotContain("balance la lumière", command.VoicePhrases);
    }

    [Fact]
    public void Une_formulation_deja_presente_ne_se_duplique_pas()
    {
        // Indexee deux fois, seule la premiere compterait : autant ne pas l'ajouter.
        PhraseAlias alias = new(
            "ship.lights.toggle", "Lumières", CommandPolarity.Neutral, DateTimeOffset.UtcNow);

        CommandCatalog enriched = UserPhrases.Apply(_catalog, [alias]);

        Assert.True(enriched.TryGet("ship.lights.toggle", out CommandDefinition? command));
        Assert.Equal(
            1, command!.VoicePhrases.Count(p => TextNormalizer.Normalize(p) == "lumieres"));
    }

    [Fact]
    public void Une_formulation_visant_une_commande_disparue_est_ignoree()
    {
        // Une commande peut disparaitre d'une version a l'autre. Cela ne justifie pas
        // qu'Optimus refuse de demarrer.
        PhraseAlias alias = new(
            "commande.qui.n.existe.plus", "peu importe",
            CommandPolarity.Neutral, DateTimeOffset.UtcNow);

        CommandCatalog enriched = UserPhrases.Apply(_catalog, [alias]);

        Assert.Equal(_catalog.Count, enriched.Count);
    }

    [Fact]
    public void Les_formulations_se_relisent_apres_ecriture()
    {
        string path = Path.Combine(_directory, "formulations.json");

        PhraseAlias[] aliases =
        [
            new("scan.ping", "envoie un ping", CommandPolarity.Neutral, DateTimeOffset.UtcNow),
            new("ship.lights.toggle", "coupe tout l'éclairage", CommandPolarity.Off, DateTimeOffset.UtcNow),
        ];

        UserPhrases.Save(path, aliases);

        IReadOnlyList<PhraseAlias> reloaded = UserPhrases.Load(path);

        Assert.Equal(2, reloaded.Count);
        Assert.Contains(reloaded, a => a.Phrase == "coupe tout l'éclairage"
                                       && a.Polarity == CommandPolarity.Off);
    }

    [Fact]
    public void Une_formulation_apprise_est_reconnue_par_le_matcher()
    {
        // C'est tout l'objet : la formulation doit reellement declencher la commande.
        PhraseAlias alias = new(
            "combat.countermeasure.decoy", "balance les leurres",
            CommandPolarity.Neutral, DateTimeOffset.UtcNow);

        FastIntentMatcher matcher = new(UserPhrases.Apply(_catalog, [alias]));

        IntentResolution resolution = matcher.Resolve(
            "Optimus, balance les leurres", wakeWord: "Optimus");

        Assert.Equal(IntentDecision.Execute, resolution.Decision);
        Assert.Equal("combat.countermeasure.decoy", resolution.Best!.Command.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
