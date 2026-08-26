using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Execution;

/// <summary>
/// Déplie les renvois d'une macro en une séquence d'étapes exécutables.
///
/// Une macro désigne des <b>commandes</b>, pas des identifiants d'action, et c'est ce qui la rend
/// fiable : chaque renvoi passe par la résolution de polarité de la commande visée — action
/// dirigée si elle a une touche, repli sur la bascule sinon. Une macro qui enchaînerait des
/// bascules brutes serait à pile ou face à chaque pas.
///
/// Le dépliage est fait <b>avant</b> l'exécution, pas pendant : le garde doit pouvoir vérifier la
/// séquence complète avant qu'une seule touche ne parte. Une macro dont le quatrième pas n'a pas
/// de raccourci ne doit pas jouer les trois premiers puis s'arrêter — le vaisseau resterait dans
/// un état intermédiaire que personne n'a demandé.
/// </summary>
public static class MacroExpander
{
    /// <summary>
    /// Profondeur maximale de renvoi.
    ///
    /// Une macro peut en appeler une autre, mais pas indéfiniment. La limite protège d'un cycle
    /// — deux macros qui s'appellent — que rien n'interdit d'écrire dans un fichier de données.
    /// </summary>
    private const int MaxDepth = 4;

    /// <summary>Étapes réellement exécutables, renvois dépliés.</summary>
    public static IReadOnlyList<ActionStep> Expand(
        CommandDefinition command,
        CommandCatalog catalog,
        BindingProfile bindings,
        CommandPolarity polarity = CommandPolarity.Neutral)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);

        List<ActionStep> expanded = new();
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        Walk(command, polarity, 0);
        return expanded;

        void Walk(CommandDefinition current, CommandPolarity sense, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"« {current.Name} » enchaîne trop de renvois : au-delà de {MaxDepth} niveaux, "
                    + "c'est presque sûrement un cycle.");
            }

            if (!visiting.Add(current.Id))
            {
                throw new InvalidOperationException(
                    $"« {current.Name} » s'appelle elle-même, directement ou non.");
            }

            foreach (ActionStep step in current.ActionsFor(sense, bindings))
            {
                if (step.Type != ActionStepType.Command)
                {
                    expanded.Add(step);
                    continue;
                }

                if (step.CommandId is null || !catalog.TryGet(step.CommandId, out CommandDefinition? target)
                    || target is null)
                {
                    throw new InvalidOperationException(
                        $"« {current.Name} » renvoie vers « {step.CommandId} », qui n'existe pas.");
                }

                Walk(target, step.Polarity, depth + 1);
            }

            visiting.Remove(current.Id);
        }
    }

    /// <summary>
    /// Actions du jeu qu'une commande finira par solliciter, renvois compris.
    ///
    /// Sert au garde et à l'inventaire des touches : une macro n'est exécutable que si
    /// <b>toutes</b> les actions qu'elle atteindra ont un raccourci.
    /// </summary>
    public static IEnumerable<string> ReachableActions(
        CommandDefinition command,
        CommandCatalog catalog,
        BindingProfile bindings,
        CommandPolarity polarity = CommandPolarity.Neutral)
    {
        IReadOnlyList<ActionStep> steps;

        try
        {
            steps = Expand(command, catalog, bindings, polarity);
        }
        catch (InvalidOperationException)
        {
            // Un catalogue incoherent se signale au chargement et a la validation ; ici on se
            // contente de ne rien promettre.
            return [];
        }

        return steps
            .Where(step => step.Type == ActionStepType.GameAction && step.ActionId is not null)
            .Select(step => step.ActionId!);
    }
}
