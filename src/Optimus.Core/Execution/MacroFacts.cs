using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Personality;

namespace Optimus.Core.Execution;

/// <summary>
/// Ce qu'Optimus sait au moment de <b>déplier</b> une macro.
///
/// Les conditions sont évaluées au dépliage, pas à l'exécution, et ce n'est pas un raccourci :
/// c'est D38. Le garde doit voir la séquence complète avant qu'une seule touche ne parte — une
/// macro dont le cinquième pas est irréalisable ne doit pas jouer les quatre premiers puis
/// s'arrêter, laissant le vaisseau à mi-chemin. Une condition évaluée en cours de route
/// rendrait le plan indéterminable, et le garde ne pourrait plus rien promettre.
///
/// D'où la <b>projection</b> : à mesure que le dépliage avance, ces faits sont mis à jour comme
/// si les pas déjà planifiés avaient été joués. Sans elle, une macro qui passe en mode combat
/// puis teste <c>si le mode est SCM</c> lirait l'état d'<i>avant</i> la macro et prendrait la
/// mauvaise branche — surprise d'autant plus désagréable qu'elle paraîtrait arbitraire.
///
/// La projection hérite naturellement des limites de ce qu'elle projette : le mode de vol et les
/// états de bascule sont des croyances (D32), et le rester après projection.
/// </summary>
public sealed class MacroFacts
{
    private readonly Dictionary<string, bool> _believed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Faits par défaut : rien de connu, touches réellement envoyées.</summary>
    public static MacroFacts Unknown { get; } = new();

    /// <summary>Mode de combat supposé.</summary>
    public bool CombatActive { get; private set; }

    /// <summary>Optimus simule-t-il l'envoi des touches ?</summary>
    public bool Simulation { get; init; }

    /// <summary>Reprend l'état supposé du vaisseau et la mémoire des bascules.</summary>
    public static MacroFacts From(
        bool combatActive, ToggleBelief belief, CommandCatalog catalog, bool simulation)
    {
        ArgumentNullException.ThrowIfNull(belief);
        ArgumentNullException.ThrowIfNull(catalog);

        MacroFacts facts = new() { Simulation = simulation, CombatActive = combatActive };

        foreach (CommandDefinition command in catalog.Commands)
        {
            if (belief.Believed(command.Id) is bool believed)
            {
                facts._believed[command.Id] = believed;
            }
        }

        return facts;
    }

    /// <summary>Copie indépendante : le dépliage projette sans altérer l'état réel.</summary>
    public MacroFacts Fork()
    {
        MacroFacts copy = new() { Simulation = Simulation, CombatActive = CombatActive };

        foreach (KeyValuePair<string, bool> entry in _believed)
        {
            copy._believed[entry.Key] = entry.Value;
        }

        return copy;
    }

    /// <summary>État supposé d'une bascule, ou <c>null</c> si Optimus n'en a jamais commuté.</summary>
    public bool? Believed(string commandId) =>
        _believed.TryGetValue(commandId, out bool value) ? value : null;

    /// <summary>
    /// Prend acte d'un pas planifié, comme s'il avait été joué.
    ///
    /// Un pas au sens neutre <b>efface</b> ce qu'on croyait savoir au lieu de l'inverser : une
    /// bascule dont on ignore l'état de départ mène à un état qu'on ignore tout autant, et
    /// prétendre le contraire serait doubler l'erreur plutôt que de l'admettre.
    /// </summary>
    public void Note(string commandId, CommandPolarity polarity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        if (string.Equals(commandId, MasterMode.CommandId, StringComparison.OrdinalIgnoreCase))
        {
            CombatActive = polarity switch
            {
                CommandPolarity.On => true,
                CommandPolarity.Off => false,
                _ => !CombatActive,
            };

            return;
        }

        switch (polarity)
        {
            case CommandPolarity.On:
                _believed[commandId] = true;
                break;

            case CommandPolarity.Off:
                _believed[commandId] = false;
                break;

            default:
                _believed.Remove(commandId);
                break;
        }
    }

    /// <summary>
    /// Tranche une condition.
    ///
    /// Une croyance absente rend la condition <b>fausse</b>, jamais vraie par défaut : entre se
    /// taire et agir sur une supposition, une macro doit se taire. La négation porte sur le
    /// verdict entier, de sorte que « si ce n'est pas supposé allumé » soit vrai aussi bien
    /// lorsque c'est éteint que lorsqu'on l'ignore.
    /// </summary>
    public bool Holds(MacroCondition condition, CommandCatalog catalog, BindingProfile bindings)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);

        bool verdict = condition.Subject switch
        {
            ConditionSubject.Simulation => Simulation,

            ConditionSubject.FlightMode =>
                CombatActive == string.Equals(condition.Value, "scm", StringComparison.OrdinalIgnoreCase),

            ConditionSubject.Believed =>
                condition.CommandId is not null
                && Believed(condition.CommandId) is bool state
                && state == string.Equals(condition.Value, "on", StringComparison.OrdinalIgnoreCase),

            ConditionSubject.Binding => Playable(condition.CommandId, catalog, bindings),

            ConditionSubject.Directed =>
                condition.CommandId is not null
                && catalog.TryGet(condition.CommandId, out CommandDefinition? directed)
                && directed is not null
                && directed.UsesDirectedActions(condition.Polarity, bindings),

            _ => false,
        };

        return condition.Negated ? !verdict : verdict;
    }

    /// <summary>
    /// Toutes les actions que cette commande finirait par solliciter ont-elles une touche ?
    ///
    /// Une commande vide — du dialogue, du lore — est jouable : elle n'a besoin d'aucune touche.
    /// </summary>
    /// <summary>
    /// Profondeur d'évaluation en cours, par fil d'exécution.
    ///
    /// Répondre à « cette commande est-elle jouable ? » demande de la déplier, et ce dépliage
    /// peut à son tour contenir une condition qui pose la même question sur une autre macro.
    /// Deux macros qui s'interrogent l'une l'autre boucleraient alors sans fin — et un
    /// débordement de pile est la pire panne possible : le processus meurt sans qu'aucun
    /// rapport ne soit écrit. Le compteur de renvois de <see cref="MacroExpander"/> ne voit
    /// rien de tout cela : chaque évaluation ouvre un dépliage neuf.
    /// </summary>
    [ThreadStatic]
    private static int _depth;

    /// <summary>
    /// Au-delà, on renonce à conclure.
    ///
    /// Une condition qui s'interroge sur elle-même n'a pas de réponse : la déclarer injouable
    /// est le refus le plus sûr, puisqu'une macro ne joue jamais ce dont elle n'est pas certaine.
    /// </summary>
    private const int MaxEvaluationDepth = 4;

    private static bool Playable(string? commandId, CommandCatalog catalog, BindingProfile bindings)
    {
        if (commandId is null
            || !catalog.TryGet(commandId, out CommandDefinition? command)
            || command is null)
        {
            return false;
        }

        if (_depth >= MaxEvaluationDepth)
        {
            return false;
        }

        _depth++;

        try
        {
            return MacroExpander
                .ReachableActions(command, catalog, bindings)
                .All(actionId => bindings.Resolve(actionId, out _) == BindingLookup.Bound);
        }
        finally
        {
            _depth--;
        }
    }
}
