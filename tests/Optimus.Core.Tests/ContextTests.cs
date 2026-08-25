using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Execution;
using Optimus.Core.Intent;
using Optimus.Core.Loading;
using Optimus.Core.Personality;

namespace Optimus.Core.Tests;

/// <summary>
/// Régressions observées en vol, sur le PC de jeu, et non en laboratoire. Chacune de ces trois
/// situations s'est produite pour de vrai avant d'être corrigée.
/// </summary>
public sealed class ContextTests
{
    private readonly CommandCatalog _catalog;

    public ContextTests()
    {
        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(TestData.RepositoryRoot, "data", "commands", "starcitizen.core.json")).Value;
    }

    [Theory]
    [InlineData("Optimus, mode scan", "scan.mode.toggle")]
    [InlineData("Optimus, mode scm", "nav.master_mode.cycle")]
    public void Une_phrase_reconnue_mot_pour_mot_s_execute_malgre_une_voisine_proche(
        string utterance, string expectedCommandId)
    {
        FastIntentMatcher matcher = new(_catalog);

        IntentResolution resolution = matcher.Resolve(utterance, wakeWord: "Optimus");

        // « mode scan » et « mode scm » se ressemblent assez pour que la seconde sorte a 0,91 -
        // dans la marge d'ambiguite. La marge bloquait donc une phrase pourtant presente telle
        // quelle dans le catalogue, et « mode scan » ne s'est jamais executee de la session.
        Assert.Equal(MatchKind.Exact, resolution.Best!.Kind);
        Assert.Equal(expectedCommandId, resolution.Best.Command.Id);
        Assert.Equal(IntentDecision.Execute, resolution.Decision);
    }

    [Theory]
    [InlineData("mode combat", true)]
    [InlineData("mode scm", true)]
    [InlineData("mode navigation", false)]
    [InlineData("mode nav", false)]
    public void Le_mode_de_vol_suit_ce_que_le_pilote_a_dit(string utterance, bool expectedCombat)
    {
        CopilotState state = new();

        // Deux fois de suite : une bascule aveugle donnerait l'inverse au second passage, ce qui
        // est exactement ce qui se produisait - dire « mode navigation » faisait croire au combat.
        Assert.Equal(expectedCombat, state.ApplyMasterMode(utterance));
        Assert.Equal(expectedCombat, state.ApplyMasterMode(utterance));
    }

    [Fact]
    public void Une_phrase_qui_ne_tranche_pas_se_contente_de_basculer()
    {
        CopilotState state = new();

        Assert.True(state.ApplyMasterMode("change de mode"));
        Assert.False(state.ApplyMasterMode("change de mode"));
    }

    [Fact]
    public void Les_libelles_lus_a_voix_haute_portent_leurs_accents()
    {
        // Les libelles sont prononces par la synthese (« Vous voulez dire {command} ? »). Sans
        // accents, Paul articulait « accuse » pour « accusé » et « repeter » pour « répéter ».
        string[] shouldBeAccented =
        [
            "system.status", "system.repeat", "dialogue.acknowledge", "ship.eject",
            "power.reset", "shields.reset", "targeting.unlock", "hud.visor_wipe",
        ];

        foreach (string id in shouldBeAccented)
        {
            Assert.True(_catalog.TryGet(id, out CommandDefinition? command));
            Assert.True(
                command!.Name.Any(c => c is 'é' or 'è' or 'ê' or 'à' or 'â' or 'É' or 'û' or 'î'),
                $"le libellé « {command.Name} » ({id}) est lu à voix haute et manque ses accents");
        }
    }

    [Theory]
    [InlineData(true, MasterMode.CombatResponseKey)]
    [InlineData(false, MasterMode.CalmResponseKey)]
    public void Le_changement_de_mode_s_annonce_dans_le_bon_sens(bool combat, string expectedKey)
    {
        Assert.True(_catalog.TryGet(MasterMode.CommandId, out CommandDefinition? command));

        ExecutionResult result = new(
            TraceId: "test",
            Status: ExecutionStatus.Simulated,
            Intent: null,
            Command: command,
            Guard: null,
            Steps: [],
            TotalMs: 0);

        // Une seule commande commute le mode dans les deux sens : « Armes chaudes » et « Retour
        // en navigation » ne peuvent donc pas se distinguer par la commande, seulement par
        // l'etat atteint. Sans cela les deux entrees ecrites pour ce cas ne servaient jamais.
        ResponseRequest? request = ResponseRouter.Route(result, new CopilotContext(CombatActive: combat));

        Assert.NotNull(request);
        Assert.Equal(expectedKey, request!.Keys[0]);
    }
}
