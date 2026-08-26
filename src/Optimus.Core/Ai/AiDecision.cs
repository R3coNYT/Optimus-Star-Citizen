using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Ai;

/// <summary>Ce que le modèle a proposé, une fois les verrous passés.</summary>
public enum AiDecisionKind
{
    /// <summary>Une commande du catalogue, validée contre la liste blanche.</summary>
    Command,

    /// <summary>Rien à exécuter : une réplique à prononcer.</summary>
    Conversation,

    /// <summary>Il manque une précision pour trancher.</summary>
    Clarification,

    /// <summary>Refusé par un verrou. <see cref="AiDecision.Rejection"/> dit lequel.</summary>
    Rejected,
}

/// <summary>Verrou ayant refusé une réponse. Journalisé tel quel.</summary>
public enum AiRejection
{
    None,

    /// <summary>Verrou 1 : la réponse n'est pas du JSON conforme.</summary>
    Malformed,

    /// <summary>Verrou 2 : l'intent ne figure pas dans la liste blanche.</summary>
    UnknownIntent,

    /// <summary>Verrou 3 : les paramètres ne respectent pas ce que la commande déclare.</summary>
    InvalidParameters,

    /// <summary>Le modèle n'a rien renvoyé, ou l'appel a échoué.</summary>
    NoAnswer,

    /// <summary>Le budget d'appels de la session est épuisé.</summary>
    BudgetSpent,
}

/// <summary>
/// Décision issue de l'étage conversationnel, après application des verrous.
///
/// Ne porte <b>jamais</b> de touche, de séquence ni d'entrée : uniquement l'identifiant d'une
/// commande du catalogue, ou du texte à dire. C'est la garantie structurelle de §73 et §75 —
/// le modèle propose une intention, le moteur seul décide de ce qu'elle déclenche.
/// </summary>
/// <param name="Kind">Nature de la décision.</param>
/// <param name="CommandId">Commande visée, validée contre le catalogue.</param>
/// <param name="Polarity">Sens demandé, quand la formulation en portait un.</param>
/// <param name="Confidence">
/// Confiance annoncée par le modèle, <b>plafonnée</b>. Elle ne peut jamais dispenser une
/// commande dangereuse de sa confirmation (verrou 5).
/// </param>
/// <param name="RequiresConfirmation">Vrai si la commande doit être confirmée avant d'agir.</param>
/// <param name="Reply">Réplique à prononcer, pour une conversation.</param>
/// <param name="Question">Question à poser, pour une demande de précision.</param>
/// <param name="Reasoning">Justification du modèle, pour le journal. Jamais prononcée.</param>
/// <param name="Rejection">Verrou ayant refusé, le cas échéant.</param>
public sealed record AiDecision(
    AiDecisionKind Kind,
    string? CommandId = null,
    CommandPolarity Polarity = CommandPolarity.Neutral,
    double Confidence = 0,
    bool RequiresConfirmation = false,
    string? Reply = null,
    string? Question = null,
    string? Reasoning = null,
    AiRejection Rejection = AiRejection.None)
{
    public static AiDecision Refused(AiRejection rejection, string? reasoning = null) =>
        new(AiDecisionKind.Rejected, Reasoning: reasoning, Rejection: rejection);
}
