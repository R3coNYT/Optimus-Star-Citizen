using Optimus.Core.Domain.Bindings;

namespace Optimus.Core.Domain.Commands;

/// <summary>
/// Nature d'une commande.
///
/// Toutes les commandes n'appuient pas sur une touche : chez Jean-Bot, près d'un cinquième du
/// catalogue est du dialogue ou du lore pur, et c'est une bonne part de ce qui donne
/// l'impression d'avoir quelqu'un à bord. Le modèle le prévoit dès l'origine.
/// </summary>
public enum CommandKind
{
    /// <summary>Une ou plusieurs entrées envoyées au jeu.</summary>
    Action,

    /// <summary>Séquence nommée, éventuellement conditionnelle.</summary>
    Macro,

    /// <summary>N'exécute rien : une réplique, pour l'immersion.</summary>
    Dialogue,

    /// <summary>N'exécute rien : consultation d'un contenu long.</summary>
    Lore,

    /// <summary>Interroge l'état interne d'Optimus (rapport système, mode simulation…).</summary>
    Query,
}

/// <summary>Type d'étape d'une séquence.</summary>
public enum ActionStepType
{
    /// <summary>Action du jeu, résolue via le profil de binding.</summary>
    GameAction,

    /// <summary>Touche brute, indépendante du jeu. Réservée aux macros utilisateur.</summary>
    Key,

    /// <summary>Attente.</summary>
    Wait,

    /// <summary>Réplique du copilote au milieu d'une séquence.</summary>
    Say,
}

/// <summary>
/// Étape élémentaire d'une séquence.
///
/// Une étape <see cref="ActionStepType.GameAction"/> ne porte <b>aucune touche</b> : uniquement
/// l'identifiant d'action. Les surcharges <see cref="Mode"/> et <see cref="HoldMs"/> permettent
/// à une commande d'imposer un maintien plus long que le défaut du binding — par exemple pour
/// charger un ping radar.
/// </summary>
public sealed record ActionStep(
    ActionStepType Type,
    string? ActionId = null,
    InputSpec? RawInput = null,
    InputMode? Mode = null,
    int? HoldMs = null,
    int Repeat = 1,
    int IntervalMs = InputSpec.DefaultIntervalMs,
    int WaitMs = 0,
    string? ResponseKey = null)
{
    public static ActionStep Game(string actionId) => new(ActionStepType.GameAction, actionId);

    public static ActionStep Wait(int milliseconds) =>
        new(ActionStepType.Wait, WaitMs: milliseconds);
}

/// <summary>
/// Définition déclarative d'une commande.
///
/// Aucun champ ne désigne une touche : c'est la garantie structurelle qu'un changement de
/// keybind ne demande jamais de toucher au catalogue.
/// </summary>
public sealed record CommandDefinition(
    string Id,
    CommandKind Kind,
    string Name,
    string Category,
    IReadOnlyList<string> VoicePhrases,
    IReadOnlyList<ActionStep> Actions,
    int CooldownMs = 0,
    bool Dangerous = false,
    string? Description = null,
    string Source = "builtin")
{
    /// <summary>Vrai si la commande n'envoie rien au jeu (dialogue, lore, requête interne).</summary>
    public bool IsPassive => Actions.Count == 0;

    /// <summary>Identifiants d'action du jeu référencés par la commande.</summary>
    public IEnumerable<string> ReferencedActionIds =>
        Actions.Where(a => a.Type == ActionStepType.GameAction && a.ActionId is not null)
               .Select(a => a.ActionId!);
}
