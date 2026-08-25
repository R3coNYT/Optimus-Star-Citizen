using Optimus.Core.Bindings;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Infrastructure.Input;

namespace Optimus.Cli;

/// <summary>Ce qu'une action représente pour le catalogue, et donc l'urgence à lui donner une touche.</summary>
internal enum ActionNeed
{
    /// <summary>Séquence par défaut d'une commande : sans touche, la commande ne marche pas.</summary>
    Primary,

    /// <summary>Sens explicite : sans touche, « éteins » retombe sur la bascule. Utile, pas vital.</summary>
    Directed,
}

/// <summary>Une action que le catalogue utilise, avec son état.</summary>
internal sealed record ActionSlot(
    string ActionId,
    string CommandId,
    string CommandName,
    ActionNeed Need,
    BindingLookup Status,
    InputSpec? Input,
    AssignmentOrigin? Origin);

/// <summary>
/// L'éditeur de keybinds.
///
/// Il existe parce qu'une installation neuve de la 4.9 laisse six actions du catalogue sans
/// aucune touche — dont l'ouverture des portes — et parce que les seize actions dirigées que le
/// jeu déclare (<c>v_lights_off</c> et consorts) n'en ont pas davantage.
///
/// <b>Le point à ne pas manquer</b> : Optimus envoie des touches, il ne parle pas à Star Citizen.
/// Assigner une touche ici ne fait donc que la moitié du chemin — le jeu ignorera la frappe tant
/// qu'il n'associe pas, de son côté, cette touche à cette action. D'où les deux sens :
/// <see cref="ImportLayout"/> pour apprendre ce que le pilote a déjà réglé dans le jeu, et
/// <see cref="ExportLayout"/> pour produire un fichier que le jeu sait relire. Sans le second,
/// l'éditeur ne serait qu'un placebo.
/// </summary>
internal static class BindingEditor
{
    /// <summary>Toutes les actions dont le catalogue a besoin, avec leur état courant.</summary>
    public static IReadOnlyList<ActionSlot> Inventory(
        CommandCatalog catalog, BindingProfile bindings, BindingOverlay overlay)
    {
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
                    assignment?.Origin));
            }
        }
    }

    /// <summary>Affiche l'inventaire, en séparant ce qui bloque de ce qui améliore.</summary>
    public static void PrintInventory(IReadOnlyList<ActionSlot> slots, BindingOverlay overlay)
    {
        ActionSlot[] missing = slots.Where(s => s.Status != BindingLookup.Bound).ToArray();
        ActionSlot[] blocking = missing.Where(s => s.Need == ActionNeed.Primary).ToArray();
        ActionSlot[] improving = missing.Where(s => s.Need == ActionNeed.Directed).ToArray();

        Console.WriteLine();
        Console.WriteLine($"  actions utilisées par le catalogue : {slots.Count}");
        Console.WriteLine($"  avec une touche                    : {slots.Count - missing.Length}");
        Console.WriteLine($"  dont assignées par vous            : {overlay.Count}");

        if (blocking.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  SANS TOUCHE - ces {blocking.Length} commandes ne peuvent pas s'exécuter :");
            foreach (ActionSlot slot in blocking)
            {
                Console.WriteLine($"    {slot.CommandName,-28} {slot.ActionId}");
            }
        }

        if (improving.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  sens explicites sans touche ({improving.Length}) - « éteins » retombe sur la bascule :");
            foreach (ActionSlot slot in improving)
            {
                Console.WriteLine($"    {slot.CommandName,-28} {slot.ActionId}");
            }
        }

        if (overlay.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  vos assignations :");
            foreach (BindingAssignment assignment in overlay.Assignments)
            {
                string origin = assignment.Origin == AssignmentOrigin.Manual ? "manuelle" : "importée";
                Console.WriteLine($"    {assignment.Input,-22} {assignment.ActionId}  ({origin})");
            }

            foreach ((string first, string second, InputSpec input) in overlay.Conflicts())
            {
                Console.WriteLine($"    CONFLIT : {input} sert à la fois à {first} et à {second}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("  --bind <action|commande>   assigner une touche en la pressant");
        Console.WriteLine("  --import-layout <fichier>  reprendre vos réglages exportés du jeu");
        Console.WriteLine("  --export-layout            produire le fichier à charger dans le jeu");
    }

    /// <summary>Assigne une touche à une action, la frappe faisant foi.</summary>
    public static async Task<int> AssignAsync(
        string target,
        IReadOnlyList<ActionSlot> slots,
        BindingOverlay overlay,
        string overlayPath)
    {
        ActionSlot[] matches = slots
            .Where(s => s.ActionId.Contains(target, StringComparison.OrdinalIgnoreCase)
                     || s.CommandId.Contains(target, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
        {
            Console.Error.WriteLine($"Aucune action ne correspond à « {target} ».");
            return 1;
        }

        if (matches.Length > 1)
        {
            Console.WriteLine($"  « {target} » désigne {matches.Length} actions :");
            foreach (ActionSlot slot in matches)
            {
                Console.WriteLine($"    {slot.ActionId}   ({slot.CommandName})");
            }

            Console.WriteLine("  Précisez laquelle.");
            return 1;
        }

        ActionSlot chosen = matches[0];

        Console.WriteLine();
        Console.WriteLine($"  action    {chosen.ActionId}");
        Console.WriteLine($"  commande  {chosen.CommandName}");
        Console.WriteLine(chosen.Input is null
            ? "  actuelle  aucune touche"
            : $"  actuelle  {chosen.Input}");
        Console.WriteLine();
        Console.WriteLine("  Pressez la touche à assigner. Échap pour renoncer.");

        using KeyCapture capture = new();
        InputSpec? captured = await capture.CaptureAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);

        if (captured is null)
        {
            Console.WriteLine("  Abandon : rien n'a été modifié.");
            return 0;
        }

        overlay.Assign(chosen.ActionId, captured, AssignmentOrigin.Manual);
        overlay.Save(overlayPath);

        Console.WriteLine();
        Console.WriteLine($"  assigné   {captured}");
        Console.WriteLine($"  écrit     {overlayPath}");
        Console.WriteLine();
        Console.WriteLine("  IMPORTANT : Optimus enverra cette touche, mais Star Citizen ne lui obéira");
        Console.WriteLine("  que s'il la connaît de son côté. Lancez « --export-layout » puis chargez le");
        Console.WriteLine("  fichier produit dans le jeu, sans quoi la frappe partira dans le vide.");

        return 0;
    }

    /// <summary>Reprend les réglages d'un fichier de mappage exporté du jeu.</summary>
    public static int ImportLayout(
        string path,
        IReadOnlyList<ActionSlot> slots,
        BindingOverlay overlay,
        string overlayPath)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Fichier introuvable : {path}");
            return 1;
        }

        LayoutImport import = ScLayoutXml.Read(path);
        HashSet<string> needed = slots.Select(s => s.ActionId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        int adopted = 0;
        int ignored = 0;

        foreach (LayoutEntry entry in import.Entries)
        {
            // Le fichier du pilote couvre tout le jeu ; le catalogue n'en utilise qu'une part.
            // Retenir le reste encombrerait sans rien apporter.
            if (!needed.Contains(entry.ActionId))
            {
                ignored++;
                continue;
            }

            overlay.Assign(entry.ActionId, entry.Input, AssignmentOrigin.ImportedLayout);
            adopted++;
        }

        overlay.Save(overlayPath);

        Console.WriteLine();
        Console.WriteLine($"  profil    {import.LayoutName ?? "sans nom"}");
        Console.WriteLine($"  lues      {import.Entries.Count} assignations");
        Console.WriteLine($"  retenues  {adopted} (utilisées par le catalogue)");
        Console.WriteLine($"  ignorées  {ignored} (hors catalogue)");

        if (import.Skipped.Count > 0)
        {
            Console.WriteLine($"  écartées  {import.Skipped.Count} :");
            foreach (string reason in import.Skipped.Take(10))
            {
                Console.WriteLine($"    {reason}");
            }

            if (import.Skipped.Count > 10)
            {
                Console.WriteLine($"    ... et {import.Skipped.Count - 10} autres");
            }
        }

        Console.WriteLine($"  écrit     {overlayPath}");
        return 0;
    }

    /// <summary>
    /// Produit le fichier de mappage à charger dans Star Citizen.
    ///
    /// C'est la moitié manquante : sans elle, une touche assignée dans Optimus part dans le vide.
    /// </summary>
    public static int ExportLayout(BindingOverlay overlay, string path)
    {
        BindingAssignment[] manual = overlay.Assignments
            .Where(a => a.Origin == AssignmentOrigin.Manual)
            .ToArray();

        if (manual.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Rien à exporter : aucune assignation manuelle.");
            Console.WriteLine("  Les réglages importés du jeu y sont déjà — les réécrire n'apporterait rien.");
            return 0;
        }

        ScLayoutXml.Save(
            ScLayoutXml.Write(manual.Select(a => new LayoutEntry(a.ActionId, a.Input)), "optimus"),
            path);

        Console.WriteLine();
        Console.WriteLine($"  écrit     {path}  ({manual.Length} assignation(s))");
        Console.WriteLine();
        Console.WriteLine("  Pour que le jeu en tienne compte :");
        Console.WriteLine("    1. copiez ce fichier dans");
        Console.WriteLine("       <Star Citizen>\\LIVE\\USER\\Client\\0\\Controls\\Mappings\\");
        Console.WriteLine("    2. dans le jeu, console (²) puis : pp_RebindKeys optimus");
        Console.WriteLine("       ou Options > Keybindings > Control Profiles > optimus");
        Console.WriteLine();
        Console.WriteLine("  Ce fichier ne contient que vos assignations : tout le reste garde");
        Console.WriteLine("  les touches par défaut du jeu.");

        return 0;
    }

    /// <summary>Traduit la couche utilisateur en surcharges applicables au profil.</summary>
    public static IReadOnlyList<Binding> ToBindings(BindingOverlay overlay) =>
        overlay.Assignments
            .Select(a => new Binding(a.ActionId, a.Input, UiLabel: null, Unsupported: false))
            .ToList();
}
