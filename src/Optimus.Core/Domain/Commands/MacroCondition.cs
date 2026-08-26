namespace Optimus.Core.Domain.Commands;

/// <summary>
/// Ce qu'une condition de macro peut interroger.
///
/// La liste est courte, et c'est délibéré : elle ne contient que ce qu'Optimus <b>sait
/// réellement</b>. Rien ne remonte du jeu (D32) — pas de carburant, pas d'altitude, pas d'état
/// de train d'atterrissage. Offrir <c>si carburant &lt; 20 %</c> reviendrait à inventer une
/// télémétrie qui n'existe pas, et une macro qui s'appuierait dessus se tromperait en silence.
///
/// Les trois premiers sujets sont <b>certains</b> : ils se lisent dans la configuration, pas
/// dans le vaisseau. Les deux derniers sont des <b>croyances</b>, et leur documentation le dit.
/// </summary>
public enum ConditionSubject
{
    /// <summary>
    /// La commande visée est-elle jouable — toutes ses actions ont-elles une touche ?
    ///
    /// Certain. C'est le sujet le plus utile : il permet à une macro de contourner ce que le
    /// pilote n'a pas configuré, au lieu d'échouer entière.
    /// </summary>
    Binding,

    /// <summary>
    /// Le <b>sens</b> demandé est-il garanti par une action dirigée du jeu ?
    ///
    /// Certain. Distinct de <see cref="Binding"/> : une commande peut être parfaitement jouable
    /// par sa bascule tout en n'offrant aucune garantie de direction (D40).
    /// </summary>
    Directed,

    /// <summary>Optimus envoie-t-il réellement les touches, ou simule-t-il ? Certain.</summary>
    Simulation,

    /// <summary>
    /// Mode de vol : <c>nav</c> ou <c>scm</c>.
    ///
    /// <b>Croyance.</b> Faute de télémétrie, Optimus se fie à ce que le pilote lui a annoncé et
    /// à ce qu'il a lui-même commuté. Que le pilote bascule au clavier, et la croyance devient
    /// fausse sans que rien ne le signale.
    /// </summary>
    FlightMode,

    /// <summary>
    /// État supposé d'une bascule : <c>on</c> ou <c>off</c>.
    ///
    /// <b>Croyance</b>, au sens de <see cref="Execution.ToggleBelief"/> : n'enregistre que les
    /// commutations qu'Optimus a lui-même provoquées. Inconnu tant qu'il n'en a provoqué aucune,
    /// auquel cas la condition est <b>fausse</b> — on ne devine pas.
    /// </summary>
    Believed,
}

/// <summary>
/// Condition d'une étape <c>si</c>.
///
/// Volontairement sans opérateurs booléens : ni <c>et</c>, ni <c>ou</c>. L'imbrication donne le
/// <c>et</c>, le <c>sinon</c> donne l'essentiel du <c>ou</c>, et une grammaire d'expressions
/// complète serait un langage de programmation dans un fichier de données — pour un besoin que
/// personne n'a encore exprimé (§70). Le jour où il s'exprimera, il sera temps.
/// </summary>
/// <param name="Subject">Ce qui est interrogé.</param>
/// <param name="Negated">Inverse le verdict. « si ce n'est pas… ».</param>
/// <param name="CommandId">
/// Commande visée, pour <see cref="ConditionSubject.Binding"/>,
/// <see cref="ConditionSubject.Directed"/> et <see cref="ConditionSubject.Believed"/>.
/// </param>
/// <param name="Polarity">Sens visé, pour <see cref="ConditionSubject.Directed"/>.</param>
/// <param name="Value">
/// Valeur attendue : <c>nav</c> ou <c>scm</c> pour le mode de vol, <c>on</c> ou <c>off</c> pour
/// un état supposé.
/// </param>
public sealed record MacroCondition(
    ConditionSubject Subject,
    bool Negated = false,
    string? CommandId = null,
    CommandPolarity Polarity = CommandPolarity.Neutral,
    string? Value = null)
{
    /// <summary>La commande visée est-elle jouable ?</summary>
    public static MacroCondition Playable(string commandId, bool negated = false) =>
        new(ConditionSubject.Binding, negated, commandId);

    /// <summary>Le sens visé est-il garanti ?</summary>
    public static MacroCondition Guaranteed(
        string commandId, CommandPolarity polarity, bool negated = false) =>
        new(ConditionSubject.Directed, negated, commandId, polarity);

    /// <summary>Le mode de vol est-il celui-ci ?</summary>
    public static MacroCondition Mode(string value, bool negated = false) =>
        new(ConditionSubject.FlightMode, negated, Value: value);

    /// <summary>La bascule est-elle supposée dans cet état ?</summary>
    public static MacroCondition State(string commandId, string value, bool negated = false) =>
        new(ConditionSubject.Believed, negated, commandId, Value: value);

    /// <summary>Optimus simule-t-il ?</summary>
    public static MacroCondition Simulating(bool negated = false) =>
        new(ConditionSubject.Simulation, negated);

    /// <summary>
    /// Formulation lisible, pour la trace et pour l'écran.
    ///
    /// Une macro qui saute une branche doit pouvoir dire <i>laquelle</i> et <i>pourquoi</i> —
    /// sans quoi le pilote ne voit qu'une séquence plus courte que prévu.
    /// </summary>
    public string Describe() => Subject switch
    {
        ConditionSubject.Binding =>
            $"« {CommandId} » {(Negated ? "n'a pas" : "a")} toutes ses touches",
        ConditionSubject.Directed =>
            $"« {CommandId} » {(Negated ? "n'offre pas" : "offre")} "
            + $"{(Polarity == CommandPolarity.Off ? "l'extinction" : "l'activation")} dirigée",
        ConditionSubject.Simulation =>
            Negated ? "les touches partent vraiment" : "Optimus simule",
        ConditionSubject.FlightMode =>
            $"le mode de vol {(Negated ? "n'est pas" : "est")} {Value?.ToUpperInvariant()}",
        ConditionSubject.Believed =>
            $"« {CommandId} » {(Negated ? "n'est pas supposé" : "est supposé")} {Value}",
        _ => "condition inconnue",
    };
}
