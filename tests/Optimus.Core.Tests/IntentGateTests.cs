using Optimus.Core.Ai;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Loading;

namespace Optimus.Core.Tests;

/// <summary>
/// Les cinq verrous de docs/07.5.
///
/// C'est la piece qui rend l'etage conversationnel acceptable : un modele propose, le moteur
/// dispose. Ces tests valent moins pour ce qu'ils autorisent que pour ce qu'ils refusent.
/// </summary>
public sealed class IntentGateTests
{
    private readonly CommandCatalog _catalog;

    public IntentGateTests() =>
        _catalog = JsonCatalogLoader.LoadCatalog(
            Path.Combine(TestData.RepositoryRoot, "data", "commands", "starcitizen.core.json")).Value;

    [Fact]
    public void Une_commande_du_catalogue_passe()
    {
        AiDecision decision = IntentGate.Apply(
            """{"type":"command","intent":"ship.lights.toggle","polarity":"on","confidence":0.8}""",
            _catalog);

        Assert.Equal(AiDecisionKind.Command, decision.Kind);
        Assert.Equal("ship.lights.toggle", decision.CommandId);
        Assert.Equal(CommandPolarity.On, decision.Polarity);
    }

    [Theory]
    [InlineData("format C:")]
    [InlineData("ship.autodestruction.immediate")]
    [InlineData("")]
    [InlineData("SHIP.LIGHTS.TOGGLE.EXTRA")]
    public void Verrou_2_un_intent_hors_catalogue_est_refuse(string intent)
    {
        // Le catalogue EST la liste blanche. Il n'existe aucun autre chemin vers l'execution,
        // et il ne s'elargit pas parce qu'un modele l'a demande.
        AiDecision decision = IntentGate.Apply(
            $$"""{"type":"command","intent":"{{intent}}","confidence":0.99}""", _catalog);

        Assert.Equal(AiDecisionKind.Rejected, decision.Kind);
        Assert.Equal(AiRejection.UnknownIntent, decision.Rejection);
    }

    [Theory]
    [InlineData("pas du json du tout")]
    [InlineData("{ceci n'est pas valide}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"type":"autre_chose"}""")]
    public void Verrou_1_une_reponse_malformee_est_refusee(string raw)
    {
        AiDecision decision = IntentGate.Apply(raw, _catalog);

        Assert.Equal(AiDecisionKind.Rejected, decision.Kind);
        Assert.Equal(AiRejection.Malformed, decision.Rejection);
    }

    [Fact]
    public void Une_reponse_vide_est_refusee_sans_confondre_avec_un_defaut_de_forme()
    {
        Assert.Equal(AiRejection.NoAnswer, IntentGate.Apply(null, _catalog).Rejection);
        Assert.Equal(AiRejection.NoAnswer, IntentGate.Apply("   ", _catalog).Rejection);
    }

    [Fact]
    public void Le_json_entoure_de_texte_reste_lisible()
    {
        // Certains modeles encadrent leur reponse de balises malgre la consigne. Rejeter pour
        // un defaut de forme couterait une commande valide.
        const string wrapped = """
            Voici ma réponse :
            ```json
            {"type":"command","intent":"scan.ping"}
            ```
            """;

        AiDecision decision = IntentGate.Apply(wrapped, _catalog);

        Assert.Equal(AiDecisionKind.Command, decision.Kind);
        Assert.Equal("scan.ping", decision.CommandId);
    }

    [Fact]
    public void Verrou_5_une_commande_dangereuse_exige_sa_confirmation()
    {
        // Meme si le modele affirme le contraire, et meme avec une confiance maximale.
        AiDecision decision = IntentGate.Apply(
            """{"type":"command","intent":"ship.self_destruct","confidence":1.0,"requires_confirmation":false}""",
            _catalog);

        Assert.Equal(AiDecisionKind.Command, decision.Kind);
        Assert.True(decision.RequiresConfirmation, "l'autodestruction ne se dispense pas de confirmation");
    }

    [Fact]
    public void Verrou_5_la_confiance_est_plafonnee()
    {
        AiDecision decision = IntentGate.Apply(
            """{"type":"command","intent":"scan.ping","confidence":42.0}""", _catalog);

        Assert.Equal(IntentGate.ConfidenceCeiling, decision.Confidence, 3);
    }

    [Fact]
    public void Verrou_3_un_sens_que_la_commande_ne_sait_pas_exprimer_est_neutralise()
    {
        // scan.ping n'a pas de sens : le transmettre le ferait retomber sur une bascule.
        AiDecision decision = IntentGate.Apply(
            """{"type":"command","intent":"scan.ping","polarity":"off"}""", _catalog);

        Assert.Equal(CommandPolarity.Neutral, decision.Polarity);
    }

    [Fact]
    public void Une_conversation_ne_porte_aucune_commande()
    {
        AiDecision decision = IntentGate.Apply(
            """{"type":"conversation","reply":"Joli vaisseau, capitaine."}""", _catalog);

        Assert.Equal(AiDecisionKind.Conversation, decision.Kind);
        Assert.Null(decision.CommandId);
        Assert.Equal("Joli vaisseau, capitaine.", decision.Reply);
    }

    [Fact]
    public void Un_catalogue_vide_ne_laisse_passer_aucune_commande()
    {
        // C'est ainsi que ConversationTier garantit qu'une conversation ne declenche rien :
        // il presente une liste blanche vide au verrou.
        AiDecision decision = IntentGate.Apply(
            """{"type":"command","intent":"ship.self_destruct","confidence":1.0}""",
            CommandCatalog.Empty);

        Assert.Equal(AiDecisionKind.Rejected, decision.Kind);
        Assert.Equal(AiRejection.UnknownIntent, decision.Rejection);
    }

    [Fact]
    public void L_invite_n_expose_que_des_identifiants_de_commande()
    {
        Copilot copilot = CopilotLoader.Load(
            Path.Combine(TestData.RepositoryRoot, "data", "copilots", "optimus")).Value;

        string prompt = AiPrompt.Resolve(_catalog, copilot);

        // Le modele ne voit JAMAIS une touche : ni scancode, ni nom de touche, ni action du jeu.
        // C'est la garantie structurelle de §73 - il ne peut pas produire ce qu'il ignore.
        Assert.DoesNotContain("v_toggle_all_doors", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("lights_controller", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("RCTRL", prompt, StringComparison.Ordinal);
        Assert.Contains("ship.lights.toggle", prompt, StringComparison.Ordinal);
    }
}
