using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Execution;

/// <summary>Résultat d'un dépliage : ce qui sera joué, et ce qui a été écarté.</summary>
/// <param name="Steps">Étapes exécutables.</param>
/// <param name="Skipped">
/// Pas écartés, avec leur raison. Jamais tus : une macro qui saute discrètement une étape
/// laisse croire qu'elle a tout fait.
/// </param>
public sealed record MacroExpansion(IReadOnlyList<ActionStep> Steps, IReadOnlyList<string> Skipped);

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
        CommandPolarity polarity = CommandPolarity.Neutral) =>
        Plan(command, catalog, bindings, polarity).Steps;

    /// <summary>Dépliage complet : les étapes retenues et celles qu'on refuse de jouer.</summary>
    public static MacroExpansion Plan(
        CommandDefinition command,
        CommandCatalog catalog,
        BindingProfile bindings,
        CommandPolarity polarity = CommandPolarity.Neutral)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);

        List<ActionStep> expanded = new();
        List<string> skipped = new();
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        Walk(command, polarity, 0);
        return new MacroExpansion(expanded, skipped);

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

                // Le pas exige un sens garanti et ne l'obtient pas : on renonce plutôt que de
                // retomber sur une bascule, qui ferait l'inverse une fois sur deux. Une
                // préparation au décollage qui OUVRE les portes au lieu de les fermer, c'est le
                // genre de surprise qu'on ne découvre qu'au moment de décoller — observé en vol
                // le 2026-08-26. Le repli reste la règle partout ailleurs : allumer un vaisseau
                // froid par une bascule est exactement ce qu'on veut.
                if (step.RequireDirected
                    && step.Polarity != CommandPolarity.Neutral
                    && !target.UsesDirectedActions(step.Polarity, bindings))
                {
                    string direction = step.Polarity == CommandPolarity.On ? "activation" : "extinction";
                    skipped.Add(
                        $"« {target.Name} » ({direction}) : le jeu n'expose pas ce sens et aucune touche "
                        + "dirigée n'est configurée. Une bascule aurait fait l'inverse une fois sur deux.");
                    continue;
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
