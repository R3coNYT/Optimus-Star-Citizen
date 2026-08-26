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

    /// <summary>
    /// Renvoi vers une autre commande du catalogue.
    ///
    /// C'est ce qui rend les macros fiables. Une macro qui enchaînerait des identifiants
    /// d'action bruts enchaînerait des <b>bascules</b> : chaque pas serait à pile ou face selon
    /// l'état du vaisseau, et une séquence de six pas n'aurait qu'une chance sur soixante-quatre
    /// de faire ce qu'on attend. En désignant une commande et un sens, la macro hérite de toute
    /// la résolution de polarité — action dirigée quand elle a une touche, repli sur la bascule
    /// sinon.
    /// </summary>
    Command,
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
    string? ResponseKey = null,
    string? CommandId = null,
    CommandPolarity Polarity = CommandPolarity.Neutral,
    bool RequireDirected = false)
{
    public static ActionStep Game(string actionId) => new(ActionStepType.GameAction, actionId);

    /// <summary>Étape qui rejoue une autre commande, dans le sens voulu.</summary>
    /// <param name="requireDirected">
    /// Exiger une action <b>dirigée</b>, et renoncer au pas plutôt que de retomber sur une
    /// bascule. À réserver aux pas dont l'état de départ est incertain : une préparation au
    /// décollage qui bascule les portes les <i>ouvre</i> une fois sur deux, et le découvrir au
    /// moment de décoller n'est pas acceptable. Ailleurs — allumer un vaisseau froid — le repli
    /// sur la bascule est au contraire ce qu'on veut.
    /// </param>
    public static ActionStep Call(
        string commandId,
        CommandPolarity polarity = CommandPolarity.Neutral,
        bool requireDirected = false) =>
        new(ActionStepType.Command, CommandId: commandId, Polarity: polarity,
            RequireDirected: requireDirected);

    public static ActionStep Wait(int milliseconds) =>
        new(ActionStepType.Wait, WaitMs: milliseconds);
}

/// <summary>
/// Sens demandé par le pilote pour une commande à deux états.
///
/// Presque toutes les commandes du jeu sont des <b>bascules</b> : une seule touche, qui inverse
/// l'état. « Éteins les lumières » envoyait donc exactement la même touche qu'« allume les
/// lumières », le mot étant compris mais la direction perdue.
///
/// Star Citizen déclare pourtant des actions dirigées — <c>v_lights_on</c> et <c>v_lights_off</c>
/// existent — mais <b>ne leur assigne aucune touche</b>. Quand l'une d'elles est configurée, on
/// s'en sert et le résultat est certain ; sinon on retombe sur la bascule, où l'on ne peut que
/// se fier à l'état supposé.
/// </summary>
public enum CommandPolarity
{
    /// <summary>La phrase ne dit pas de sens : « lumières », « mode scan ». On bascule.</summary>
    Neutral,

    /// <summary>« Allume », « active », « sors », « ouvre ».</summary>
    On,

    /// <summary>« Éteins », « désactive », « rentre », « ferme ».</summary>
    Off,
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
    /// <summary>Formulations qui demandent explicitement l'activation.</summary>
    public IReadOnlyList<string> PhrasesOn { get; init; } = Array.Empty<string>();

    /// <summary>Formulations qui demandent explicitement l'extinction.</summary>
    public IReadOnlyList<string> PhrasesOff { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Séquence dirigée vers l'activation, quand le jeu en déclare une. Souvent présente mais
    /// sans touche assignée : c'est à l'éditeur de keybinds de la rendre utilisable.
    /// </summary>
    public IReadOnlyList<ActionStep> ActionsOn { get; init; } = Array.Empty<ActionStep>();

    /// <summary>Séquence dirigée vers l'extinction, mêmes réserves.</summary>
    public IReadOnlyList<ActionStep> ActionsOff { get; init; } = Array.Empty<ActionStep>();

    /// <summary>Vrai si la commande n'envoie rien au jeu (dialogue, lore, requête interne).</summary>
    public bool IsPassive => Actions.Count == 0;

    /// <summary>Vrai si le pilote peut en demander explicitement le sens.</summary>
    public bool HasPolarity => PhrasesOn.Count > 0 || PhrasesOff.Count > 0;

    /// <summary>Toutes les formulations reconnues, tous sens confondus.</summary>
    public IEnumerable<string> AllPhrases => VoicePhrases.Concat(PhrasesOn).Concat(PhrasesOff);

    /// <summary>Séquence dirigée déclarée pour ce sens, vide s'il n'y en a pas.</summary>
    public IReadOnlyList<ActionStep> DirectedActions(CommandPolarity polarity) => polarity switch
    {
        CommandPolarity.On => ActionsOn,
        CommandPolarity.Off => ActionsOff,
        _ => Array.Empty<ActionStep>(),
    };

    /// <summary>
    /// Séquence à exécuter pour ce sens.
    ///
    /// La séquence dirigée n'est retenue que si <b>toutes</b> ses actions ont une touche : une
    /// action déclarée par le jeu mais non assignée ne vaut rien, et il est bien préférable de
    /// retomber sur la bascule que de refuser la commande.
    /// </summary>
    public IReadOnlyList<ActionStep> ActionsFor(CommandPolarity polarity, BindingProfile bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        IReadOnlyList<ActionStep> directed = DirectedActions(polarity);

        if (directed.Count > 0 && directed.All(step =>
                step.Type != ActionStepType.GameAction ||
                step.ActionId is null ||
                bindings.Resolve(step.ActionId, out _) == BindingLookup.Bound))
        {
            return directed;
        }

        return Actions;
    }

    /// <summary>
    /// Vrai si ce sens dispose d'une séquence dirigée réellement utilisable.
    ///
    /// Une action dirigée est <b>idempotente</b> : la renvoyer ne peut pas nuire, il n'y a donc
    /// rien à supposer de l'état du vaisseau. C'est seulement sur une bascule qu'un appui de
    /// trop fait l'inverse de ce qu'on demande.
    /// </summary>
    public bool UsesDirectedActions(CommandPolarity polarity, BindingProfile bindings)
    {
        IReadOnlyList<ActionStep> directed = DirectedActions(polarity);

        return directed.Count > 0 && ReferenceEquals(ActionsFor(polarity, bindings), directed);
    }


    /// <summary>
    /// Actions du jeu de la séquence par défaut.
    ///
    /// Volontairement <b>sans</b> les séquences dirigées : celles-ci sont presque toutes sans
    /// touche, et le garde qui exige un binding rejetterait sinon toute commande qui en déclare
    /// une. Elles ne sont exigées que lorsqu'elles sont réellement retenues, par
    /// <see cref="ActionsFor"/>.
    /// </summary>
    public IEnumerable<string> ReferencedActionIds => ActionIdsOf(Actions);

    /// <summary>Toutes les actions référencées, tous sens confondus : validation et outillage.</summary>
    public IEnumerable<string> AllReferencedActionIds =>
        ActionIdsOf(Actions).Concat(ActionIdsOf(ActionsOn)).Concat(ActionIdsOf(ActionsOff));

    private static IEnumerable<string> ActionIdsOf(IReadOnlyList<ActionStep> steps) =>
        steps.Where(a => a.Type == ActionStepType.GameAction && a.ActionId is not null)
             .Select(a => a.ActionId!);
}
