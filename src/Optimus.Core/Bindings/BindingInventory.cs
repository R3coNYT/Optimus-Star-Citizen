using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;

namespace Optimus.Core.Bindings;

/// <summary>Ce qu'une action représente pour le catalogue, et donc l'urgence à lui donner une touche.</summary>
public enum ActionNeed
{
    /// <summary>Séquence par défaut d'une commande : sans touche, la commande ne marche pas.</summary>
    Primary,

    /// <summary>Sens explicite : sans touche, « éteins » retombe sur la bascule. Utile, pas vital.</summary>
    Directed,
}

/// <summary>Une action que le catalogue utilise, avec son état.</summary>
/// <param name="ActionId">Identifiant <c>actionmap/action</c>.</param>
/// <param name="CommandId">Commande qui s'en sert.</param>
/// <param name="CommandName">Libellé de cette commande.</param>
/// <param name="Need">Bloquante ou simple amélioration.</param>
/// <param name="Status">État de la résolution, assignations du pilote comprises.</param>
/// <param name="Input">Entrée effective, s'il y en a une.</param>
/// <param name="Origin">Origine de l'assignation, quand elle vient du pilote.</param>
/// <param name="SearchText">
/// Identifiants, libellé et phrases vocales, normalisés. Le pilote cherche par ce qu'il lit ou
/// par ce qu'il dit — « lumieres » doit trouver « Feux du vaisseau » — jamais par l'identifiant.
/// </param>
public sealed record ActionSlot(
    string ActionId,
    string CommandId,
    string CommandName,
    ActionNeed Need,
    BindingLookup Status,
    InputSpec? Input,
    AssignmentOrigin? Origin,
    string SearchText)
{
    /// <summary>Vrai lorsque l'action dispose d'une touche exploitable.</summary>
    public bool IsBound => Status == BindingLookup.Bound;
}

/// <summary>
/// Inventaire des actions dont le catalogue a besoin.
///
/// Partagé entre le banc d'essai et l'interface : la liste de ce qui manque, l'ordre dans lequel
/// le traiter et la façon de le chercher sont les mêmes des deux côtés, et doivent le rester.
/// </summary>
public static class BindingInventory
{
    /// <summary>Toutes les actions utilisées, bloquantes d'abord.</summary>
    public static IReadOnlyList<ActionSlot> Build(
        CommandCatalog catalog, BindingProfile bindings, BindingOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(overlay);

        List<ActionSlot> slots = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (CommandDefinition command in catalog.Commands)
        {
            Collect(command, command.Actions, ActionNeed.Primary);
            Collect(command, command.ActionsOn, ActionNeed.Directed);
            Collect(command, command.ActionsOff, ActionNeed.Directed);
        }

        return slots.OrderBy(s => s.Need).ThenBy(s => s.ActionId, StringComparer.Ordinal).ToList();

        void Collect(CommandDefinition command, IReadOnlyList<ActionStep> steps, ActionNeed need)
        {
            foreach (ActionStep step in steps)
            {
                if (step.Type != ActionStepType.GameAction || step.ActionId is null || !seen.Add(step.ActionId))
                {
                    continue;
                }

                BindingAssignment? assignment = overlay.Find(step.ActionId);
                BindingLookup status = bindings.Resolve(step.ActionId, out Binding? binding);

                slots.Add(new ActionSlot(
                    step.ActionId,
                    command.Id,
                    command.Name,
                    need,
                    assignment is not null ? BindingLookup.Bound : status,
                    assignment?.Input ?? binding?.Input,
                    assignment?.Origin,
                    TextNormalizer.Normalize(
                        $"{step.ActionId} {command.Id} {command.Name} {string.Join(' ', command.AllPhrases)}")));
            }
        }
    }

    /// <summary>Actions correspondant à un terme de recherche, accents ignorés.</summary>
    public static IReadOnlyList<ActionSlot> Search(IEnumerable<ActionSlot> slots, string term)
    {
        ArgumentNullException.ThrowIfNull(slots);

        string needle = TextNormalizer.Normalize(term ?? string.Empty);

        if (needle.Length == 0)
        {
            return slots.ToList();
        }

        List<ActionSlot> matches = slots
            .Where(slot => slot.SearchText.Contains(needle, StringComparison.Ordinal))
            .ToList();

        // Un libellé qui correspond exactement tranche : « boucliers » désigne la bascule, même
        // si une douzaine d'actions portent le mot.
        List<ActionSlot> exact = matches
            .Where(slot => TextNormalizer.Normalize(slot.CommandName) == needle)
            .ToList();

        return exact.Count == 1 ? exact : matches;
    }
}
