using Optimus.Core.Diagnostics;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Copilots;

namespace Optimus.Core.Ai;

/// <summary>Réglages de l'étage conversationnel.</summary>
/// <param name="Enabled">Faux par défaut : Optimus fonctionne sans, et c'est une exigence (§84).</param>
/// <param name="Provider">Fournisseur : <c>ollama</c>, <c>openai</c>, ou vide.</param>
/// <param name="Endpoint">Adresse du service.</param>
/// <param name="Model">Modèle demandé.</param>
/// <param name="CallBudget">
/// Nombre d'appels autorisés par session. Un modèle distant se facture, un modèle local coûte
/// du temps : dans les deux cas, une boucle qui s'emballe doit s'arrêter d'elle-même.
/// </param>
public sealed record AiSettings(
    bool Enabled = false,
    string Provider = "ollama",
    string Endpoint = "http://localhost:11434",
    string Model = "llama3.1",
    int CallBudget = 200)
{
    public static AiSettings Disabled { get; } = new();
}

/// <summary>Ce que l'étage conversationnel a produit.</summary>
/// <param name="Decision">Décision, verrous appliqués.</param>
/// <param name="ElapsedMs">Durée de l'appel, pour savoir si le fournisseur suit.</param>
public sealed record AiOutcome(AiDecision Decision, double ElapsedMs);

/// <summary>
/// L'étage conversationnel.
///
/// N'intervient qu'en <b>dernier recours</b> : quand le catalogue et la grammaire n'ont rien su
/// faire de ce qui a été dit. C'est délibéré et pas seulement économique — le chemin rapide est
/// déterministe, testé et instantané, là où un modèle est lent, variable, et faillible. Lui
/// donner la main plus tôt échangerait de la fiabilité contre de la souplesse.
///
/// Tout ce qu'il rend passe par <see cref="IntentGate"/>. Le modèle ne voit jamais une touche,
/// n'a aucun accès au moteur d'entrée, et ne peut désigner qu'une commande déjà présente au
/// catalogue.
/// </summary>
public sealed class ConversationTier(ILanguageModel model, AiSettings settings)
{
    private int _calls;

    /// <summary>Appels consommés depuis le démarrage.</summary>
    public int Calls => _calls;

    /// <summary>Appels restants avant épuisement du budget.</summary>
    public int Remaining => Math.Max(0, settings.CallBudget - _calls);

    public string ModelId => model.Id;

    /// <summary>
    /// Tente de comprendre un énoncé que le chemin rapide a laissé passer.
    /// </summary>
    public async Task<AiOutcome> ResolveAsync(
        string utterance,
        CommandCatalog catalog,
        Copilot copilot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(utterance);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(copilot);

        return await AskAsync(
            new LanguageRequest(AiPrompt.Resolve(catalog, copilot), utterance),
            catalog,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Demande une réplique, sans qu'aucune exécution soit possible.
    ///
    /// Le catalogue passé au verrou est vide : même si le modèle renvoyait une commande, elle
    /// serait refusée. Une conversation ne déclenche rien, par construction et pas par égard.
    /// </summary>
    public async Task<AiOutcome> ConverseAsync(
        string utterance,
        Copilot copilot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(utterance);
        ArgumentNullException.ThrowIfNull(copilot);

        return await AskAsync(
            new LanguageRequest(AiPrompt.Converse(copilot), utterance, Temperature: 0.7),
            CommandCatalog.Empty,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AiOutcome> AskAsync(
        LanguageRequest request, CommandCatalog whitelist, CancellationToken cancellationToken)
    {
        if (Remaining == 0)
        {
            DiagnosticLog.Warn(
                "budget d'appels épuisé",
                $"{settings.CallBudget} appels consommés depuis le démarrage");

            return new AiOutcome(AiDecision.Refused(AiRejection.BudgetSpent), 0);
        }

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _calls);

        string? raw = await model.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

        double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                         * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        AiDecision decision = IntentGate.Apply(raw, whitelist);

        // Un refus se journalise toujours : c'est la trace qui dira, le jour venu, qu'un modele
        // a propose autre chose que ce qu'on lui autorisait.
        if (decision.Kind == AiDecisionKind.Rejected)
        {
            DiagnosticLog.Warn(
                $"proposition refusée par le verrou ({decision.Rejection})",
                decision.Reasoning);
        }

        return new AiOutcome(decision, elapsed);
    }
}
