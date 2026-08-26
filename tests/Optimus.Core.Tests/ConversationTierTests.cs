using Optimus.Core.Ai;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Copilots;

namespace Optimus.Core.Tests;

/// <summary>
/// L'étage conversationnel, éprouvé sans modèle réel.
///
/// Ce qu'on vérifie ici n'est pas la qualité des réponses — elle appartient au modèle — mais le
/// fait qu'aucune d'elles ne puisse dépasser ce qui lui est permis. Un modèle hostile est donc
/// un meilleur banc d'essai qu'un modèle compétent : celui de ces essais répond exactement ce
/// qu'on lui dicte, y compris ce qu'il ne devrait jamais répondre.
/// </summary>
public sealed class ConversationTierTests
{
    /// <summary>Un modèle qui rend ce qu'on lui a dicté, et compte ses interrogations.</summary>
    private sealed class ScriptedModel(params string?[] answers) : ILanguageModel
    {
        private int _index;

        public string Id => "essai:scripté";

        public int Calls { get; private set; }

        /// <summary>La dernière consigne reçue, pour inspecter ce qui est exposé au modèle.</summary>
        public string? LastSystem { get; private set; }

        public Task<bool> IsReachableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<string?> CompleteAsync(
            LanguageRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSystem = request.System;

            string? answer = _index < answers.Length ? answers[_index] : answers[^1];
            _index++;

            return Task.FromResult(answer);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static CommandCatalog Catalog() => new("essai", "Catalogue d'essai",
    [
        new CommandDefinition(
            "lights", CommandKind.Action, "Feux", "vaisseau",
            ["les feux"], [ActionStep.Game("lights_toggle")])
        {
            PhrasesOn = ["allume les feux"],
            PhrasesOff = ["éteins les feux"],
            ActionsOn = [ActionStep.Game("lights_on")],
            ActionsOff = [ActionStep.Game("lights_off")],
        },
        new CommandDefinition(
            "self_destruct", CommandKind.Action, "Autodestruction", "urgence",
            ["autodestruction"], [ActionStep.Game("self_destruct")], Dangerous: true),
    ]);

    private static Copilot Pilot() => Copilot.Fallback;

    [Fact]
    public async Task Une_commande_du_catalogue_traverse_l_etage()
    {
        ScriptedModel model = new(
            """{"type":"command","intent":"lights","polarity":"off","confidence":0.8}""");

        ConversationTier tier = new(model, new AiSettings(Enabled: true));

        AiOutcome outcome = await tier.ResolveAsync("coupe-moi ces phares", Catalog(), Pilot());

        Assert.Equal(AiDecisionKind.Command, outcome.Decision.Kind);
        Assert.Equal("lights", outcome.Decision.CommandId);
        Assert.Equal(CommandPolarity.Off, outcome.Decision.Polarity);
    }

    /// <summary>
    /// Le cas qui compte : converser ne doit rien pouvoir déclencher.
    ///
    /// On donne au modèle la seule réponse qui pourrait faire des dégâts — une commande
    /// dangereuse, pleinement formée, avec une confiance maximale — au beau milieu d'un échange
    /// où le pilote ne demandait rien. La liste blanche vide la refuse sans la lire.
    /// </summary>
    [Fact]
    public async Task Converser_ne_peut_rien_declencher_meme_si_le_modele_l_ordonne()
    {
        ScriptedModel model = new(
            """{"type":"command","intent":"self_destruct","polarity":"on","confidence":1.0}""");

        ConversationTier tier = new(model, new AiSettings(Enabled: true));

        AiOutcome outcome = await tier.ConverseAsync("tu penses quoi de ce vaisseau ?", Pilot());

        Assert.Equal(AiDecisionKind.Rejected, outcome.Decision.Kind);
        Assert.Equal(AiRejection.UnknownIntent, outcome.Decision.Rejection);
        Assert.Null(outcome.Decision.CommandId);
    }

    /// <summary>
    /// Une consigne de conversation ne mentionne aucune commande.
    ///
    /// Le verrou suffirait, mais autant ne pas suggérer au modèle qu'un déclenchement existe :
    /// on ne pose pas la tentation pour ensuite la refuser.
    /// </summary>
    [Fact]
    public async Task La_consigne_de_conversation_n_enumere_aucune_commande()
    {
        ScriptedModel model = new("""{"type":"conversation","reply":"Vaisseau solide."}""");

        ConversationTier tier = new(model, new AiSettings(Enabled: true));

        await tier.ConverseAsync("tu penses quoi de ce vaisseau ?", Pilot());

        Assert.DoesNotContain("self_destruct", model.LastSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("lights", model.LastSystem, StringComparison.Ordinal);
    }

    /// <summary>
    /// Le budget arrête une boucle qui s'emballe.
    ///
    /// Un modèle distant se facture, un modèle local occupe le processeur pendant que le pilote
    /// vole : dans les deux cas, l'épuisement doit être silencieux et sans appel, pas une panne.
    /// </summary>
    [Fact]
    public async Task Le_budget_epuise_arrete_les_appels()
    {
        ScriptedModel model = new("""{"type":"conversation","reply":"Bien reçu."}""");

        ConversationTier tier = new(model, new AiSettings(Enabled: true, CallBudget: 2));

        await tier.ConverseAsync("premier", Pilot());
        await tier.ConverseAsync("deuxième", Pilot());
        AiOutcome third = await tier.ConverseAsync("troisième", Pilot());

        Assert.Equal(2, model.Calls);
        Assert.Equal(0, tier.Remaining);
        Assert.Equal(AiDecisionKind.Rejected, third.Decision.Kind);
        Assert.Equal(AiRejection.BudgetSpent, third.Decision.Rejection);
    }

    /// <summary>
    /// Un fournisseur muet ne fait rien tomber.
    ///
    /// Ollama arrêté, réseau coupé, modèle absent : <c>CompleteAsync</c> rend <c>null</c>, et
    /// l'étage doit se contenter d'un refus. Optimus retombe alors sur son catalogue, ce qui est
    /// exactement l'état dans lequel il vivait avant que cet étage existe.
    /// </summary>
    [Fact]
    public async Task Un_fournisseur_muet_donne_un_refus_et_non_une_exception()
    {
        ScriptedModel model = new([null]);

        ConversationTier tier = new(model, new AiSettings(Enabled: true));

        AiOutcome outcome = await tier.ResolveAsync("quelque chose", Catalog(), Pilot());

        Assert.Equal(AiDecisionKind.Rejected, outcome.Decision.Kind);
        Assert.Equal(AiRejection.NoAnswer, outcome.Decision.Rejection);
    }
}
