using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Execution;

/// <summary>Résultat d'un dépliage : ce qui sera joué, et ce qui a été écarté.</summary>
/// <param name="Steps">Étapes exécutables.</param>
/// <param name="Skipped">
/// Pas écartés, avec leur raison. Jamais tus : une macro qui saute discrètement une étape
/// laisse croire qu'elle a tout fait.
/// </param>
/// <param name="Decisions">
/// Branches tranchees, avec leur raison. Distinct de <paramref name="Skipped"/>, et la
/// distinction compte : un pas ecarte est un pas qu'Optimus a <b>refuse</b> de jouer, une
/// branche tranchee est un choix normal. Les confondre ferait lire un refus la ou il n'y a
/// qu'un « sinon ».
/// </param>
public sealed record MacroExpansion(
    IReadOnlyList<ActionStep> Steps,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Decisions);

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

    /// <summary>
    /// Repetitions maximales d'un bloc.
    ///
    /// Une macro n'est pas un script de traitement : au-dela d'une vingtaine de tours, ce n'est
    /// plus une sequence de vol mais une boucle qui monopolise le clavier du pilote pendant que
    /// le vaisseau vole. La borne est refusee explicitement plutot que tronquee en silence.
    /// </summary>
    public const int MaxRepeat = 20;

    /// <summary>
    /// Etapes maximales dans le plan deplie.
    ///
    /// Les repetitions imbriquees se multiplient : trois boucles de vingt font huit mille pas
    /// sans qu'aucune borne individuelle ne soit franchie. Ce plafond attrape ce que
    /// <see cref="MaxRepeat"/> ne peut pas voir.
    /// </summary>
    public const int MaxSteps = 400;

    /// <summary>Étapes réellement exécutables, renvois dépliés.</summary>
    public static IReadOnlyList<ActionStep> Expand(
        CommandDefinition command,
        CommandCatalog catalog,
        BindingProfile bindings,
        CommandPolarity polarity = CommandPolarity.Neutral,
        MacroFacts? facts = null) =>
        Plan(command, catalog, bindings, polarity, facts).Steps;

    /// <summary>Dépliage complet : les étapes retenues, celles qu'on refuse, les branches tranchées.</summary>
    /// <param name="facts">
    /// Ce qu'Optimus sait au moment de planifier. Omis, les conditions sont tranchées sur des
    /// faits vierges — aucune croyance, touches réellement envoyées — ce qui rend le dépliage
    /// déterministe pour la validation et le banc d'essai.
    /// </param>
    public static MacroExpansion Plan(
        CommandDefinition command,
        CommandCatalog catalog,
        BindingProfile bindings,
        CommandPolarity polarity = CommandPolarity.Neutral,
        MacroFacts? facts = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);

        List<ActionStep> expanded = new();
        List<string> skipped = new();
        List<string> decisions = new();
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        // Une copie : planifier ne doit rien changer a ce qu'Optimus croit reellement. Les
        // commutations ne sont enregistrees qu'une fois jouees, par l'executeur.
        MacroFacts projected = (facts ?? MacroFacts.Unknown).Fork();

        Walk(command, polarity, 0);
        return new MacroExpansion(expanded, skipped, decisions);

        void Walk(CommandDefinition current, CommandPolarity sense, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"“{current.Name}” chains too many references: beyond {MaxDepth} levels, "
                    + "it is almost certainly a cycle.");
            }

            if (!visiting.Add(current.Id))
            {
                throw new InvalidOperationException(
                    $"“{current.Name}” calls itself, directly or otherwise.");
            }

            Play(current.ActionsFor(sense, bindings), current, depth);

            visiting.Remove(current.Id);
        }

        void Play(IReadOnlyList<ActionStep> steps, CommandDefinition current, int depth)
        {
            foreach (ActionStep step in steps)
            {
                switch (step.Type)
                {
                    case ActionStepType.If:
                        Branch(step, current, depth);
                        break;

                    case ActionStepType.Repeat:
                        Loop(step, current, depth);
                        break;

                    case ActionStepType.Command:
                        Call(step, current, depth);
                        break;

                    default:
                        Emit(step);
                        break;
                }
            }
        }

        void Branch(ActionStep step, CommandDefinition current, int depth)
        {
            if (step.Condition is not MacroCondition condition)
            {
                throw new InvalidOperationException(
                    $"« {current.Name} » contient un « si » sans condition.");
            }

            bool holds = projected.Holds(condition, catalog, bindings);
            IReadOnlyList<ActionStep> chosen = holds ? step.Block : step.Alternative;

            // Dire la branche prise, meme quand elle est vide : une macro qui joue trois pas la
            // ou le pilote en attendait cinq doit pouvoir s'expliquer autrement que par un
            // decompte.
            decisions.Add(
                $"{condition.Describe()} → {(holds ? "yes" : "no")}"
                + (chosen.Count == 0 ? ", nothing to play" : $", {chosen.Count} step{(chosen.Count == 1 ? string.Empty : "s")}"));

            Play(chosen, current, depth);
        }

        void Loop(ActionStep step, CommandDefinition current, int depth)
        {
            if (step.Repeat < 1 || step.Repeat > MaxRepeat)
            {
                throw new InvalidOperationException(
                    $"“{current.Name}” repeats a block {step.Repeat} times: the count must be "
                    + $"compris entre 1 et {MaxRepeat}.");
            }

            for (int turn = 0; turn < step.Repeat; turn++)
            {
                Play(step.Block, current, depth);
            }
        }

        void Call(ActionStep step, CommandDefinition current, int depth)
        {
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
                    + "directed binding is configured. A toggle would have done the opposite half the time.");
                return;
            }

            Walk(target, step.Polarity, depth + 1);

            // Le pas est planifie : la suite du depliage doit le tenir pour joue, sans quoi un
            // « si » place apres lui lirait l'etat d'avant la macro.
            projected.Note(target.Id, step.Polarity);
        }

        void Emit(ActionStep step)
        {
            if (expanded.Count >= MaxSteps)
            {
                throw new InvalidOperationException(
                    $"“{command.Name}” expands to more than {MaxSteps} steps. Nested repetitions "
                    + "multiply without any single one going past its own bound.");
            }

            expanded.Add(step);
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
