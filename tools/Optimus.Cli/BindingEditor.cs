using Optimus.Core.Bindings;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;
using Optimus.Infrastructure.Input;

namespace Optimus.Cli;

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
        CommandCatalog catalog, BindingProfile bindings, BindingOverlay overlay) =>
        BindingInventory.Build(catalog, bindings, overlay);

    /// <summary>Affiche l'inventaire, en séparant ce qui bloque de ce qui améliore.</summary>
    public static void PrintInventory(
        IReadOnlyList<ActionSlot> slots, BindingOverlay overlay, string? mappingsDirectory = null)
    {
        ActionSlot[] missing = slots.Where(s => !s.IsBound).ToArray();
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

        if (mappingsDirectory is not null && Directory.Exists(mappingsDirectory))
        {
            Console.WriteLine();
            Console.WriteLine($"  profils du jeu : {mappingsDirectory}");
        }

        Console.WriteLine();
        Console.WriteLine("  --bind                     passer en revue tout ce qui manque");
        Console.WriteLine("  --bind <action|commande>   assigner une seule touche");
        Console.WriteLine("  --import-layout <fichier>  reprendre vos réglages exportés du jeu");
        Console.WriteLine("  --export-layout            produire le fichier à charger dans le jeu");
        Console.WriteLine("  --unbind [action]          défaire une assignation");
        Console.WriteLine("  --reset-bindings           tout défaire, après confirmation");
    }

    /// <summary>Assigne une touche à une action, la frappe faisant foi.</summary>
    public static async Task<int> AssignAsync(
        string target,
        IReadOnlyList<ActionSlot> slots,
        BindingOverlay overlay,
        string overlayPath)
    {
        // On cherche dans le libelle ET dans les phrases vocales, sans accents : le pilote
        // tape ce qu'il lit ou ce qu'il dit, jamais l'identifiant technique. « --bind portes »
        // doit trouver « Portes du vaisseau », et « --bind lumieres » « Feux du vaisseau ».
        ActionSlot[] matches = BindingInventory.Search(slots, target).ToArray();

        if (matches.Length == 0)
        {
            Console.Error.WriteLine($"Aucune action ne correspond à « {target} ».");
            return 1;
        }

        ActionSlot? selected = matches.Length == 1 ? matches[0] : Choose(target, matches);

        if (selected is null)
        {
            return 1;
        }

        ActionSlot chosen = selected;

        Console.WriteLine();
        Console.WriteLine($"  action    {chosen.ActionId}");
        Console.WriteLine($"  commande  {chosen.CommandName}");
        Console.WriteLine(chosen.Input is null
            ? "  actuelle  aucune touche"
            : $"  actuelle  {chosen.Input}");
        Console.WriteLine();
        Console.WriteLine("  Pressez la touche à assigner. Échap pour renoncer.");

        InputSpec? captured;

        try
        {
            using KeyCapture capture = new();
            captured = await capture.CaptureAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            // Erreur d'usage, pas defaut du programme : une trace de pile n'apprendrait rien.
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  {exception.Message}");
            return 1;
        }

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


    /// <summary>
    /// Passe en revue tout ce qui n'a pas de touche, l'un après l'autre.
    ///
    /// Vingt et une actions à configurer, c'est vingt et une invocations séparées — le genre de
    /// corvée qu'on remet indéfiniment. Les bloquantes viennent d'abord : ce sont les seules qui
    /// empêchent une commande de fonctionner, les sens explicites ne font qu'améliorer.
    /// </summary>
    public static async Task<int> AssignAllAsync(
        IReadOnlyList<ActionSlot> slots,
        BindingOverlay overlay,
        string overlayPath)
    {
        ActionSlot[] pending = slots.Where(s => !s.IsBound).ToArray();

        if (pending.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Tout est configuré : rien à assigner.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {pending.Length} action(s) sans touche.");
        Console.WriteLine("  Pressez la touche voulue, ou Échap pour passer à la suivante.");
        Console.WriteLine("  Ctrl+C pour arrêter là : ce qui est déjà assigné reste enregistré.");

        int assigned = 0;
        int skipped = 0;

        foreach (ActionSlot slot in pending)
        {
            string kind = slot.Need == ActionNeed.Primary ? "BLOQUANTE" : "sens explicite";

            Console.WriteLine();
            Console.WriteLine($"  [{assigned + skipped + 1}/{pending.Length}] {slot.CommandName}   ({kind})");
            Console.WriteLine($"        {slot.ActionId}");
            Console.Write("        touche : ");

            InputSpec? captured;

            try
            {
                using KeyCapture capture = new();
                captured = await capture.CaptureAsync(TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"  {exception.Message}");
                return 1;
            }

            if (captured is null)
            {
                Console.WriteLine("passée");
                skipped++;
                continue;
            }

            // Une touche deja prise ailleurs se signale sans rien refuser : Star Citizen lui-meme
            // partage des touches entre contextes, et le pilote sait ce qu'il fait.
            string combination = BindingOverlay.Combination(captured);
            BindingAssignment? clash = overlay.Assignments.FirstOrDefault(
                a => BindingOverlay.Combination(a.Input) == combination);

            overlay.Assign(slot.ActionId, captured, AssignmentOrigin.Manual);
            overlay.Save(overlayPath);
            assigned++;

            Console.WriteLine(clash is null
                ? $"{captured}"
                : $"{captured}   (déjà utilisée par {clash.ActionId})");
        }

        Console.WriteLine();
        Console.WriteLine($"  {assigned} assignée(s), {skipped} passée(s).");
        Console.WriteLine($"  écrit     {overlayPath}");

        if (assigned > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Reste à les faire connaître au jeu : --export-layout");
        }

        return 0;
    }

    /// <summary>Reprend les réglages d'un fichier de mappage exporté du jeu.</summary>
    public static int ImportLayout(
        string? path,
        IReadOnlyList<ActionSlot> slots,
        BindingOverlay overlay,
        string overlayPath,
        string? mappingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            path = NewestLayout(mappingsDirectory);

            if (path is null)
            {
                PrintWhereLayoutsLive(mappingsDirectory);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine($"  trouvé    {path}");
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Fichier introuvable : {path}");
            PrintWhereLayoutsLive(mappingsDirectory);
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
    public static int ExportLayout(BindingOverlay overlay, string path, bool inGameFolder)
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
        if (inGameFolder)
        {
            Console.WriteLine("  Le fichier est déjà dans le dossier du jeu. Il ne reste qu'à le charger :");
            Console.WriteLine("    dans Star Citizen, console (²) puis : pp_RebindKeys optimus");
            Console.WriteLine("    ou Options > Keybindings > Control Profiles > optimus");
        }
        else
        {
            Console.WriteLine("  Pour que le jeu en tienne compte :");
            Console.WriteLine("    1. copiez ce fichier dans");
            Console.WriteLine(@"       <Star Citizen>\LIVE\USER\Client\0\Controls\Mappings\");
            Console.WriteLine("    2. dans le jeu, console (²) puis : pp_RebindKeys optimus");
            Console.WriteLine("       ou Options > Keybindings > Control Profiles > optimus");
        }

        Console.WriteLine();
        Console.WriteLine("  Ce fichier ne contient que vos assignations : tout le reste garde");
        Console.WriteLine("  les touches par défaut du jeu.");

        return 0;
    }




    /// <summary>
    /// Demande laquelle, quand un terme en désigne plusieurs.
    ///
    /// « portes » vaut aussi bien pour les portes du vaisseau que pour le verrouillage des sas,
    /// et « lumières » recouvre la bascule et ses deux sens. Renvoyer le pilote à la ligne de
    /// commande pour qu'il recopie un identifiant serait gratuitement raide : on est déjà en
    /// console, et l'état de chacune tient sur une ligne.
    /// </summary>
    private static ActionSlot? Choose(string target, ActionSlot[] matches)
    {
        Console.WriteLine();
        Console.WriteLine($"  « {target} » désigne {matches.Length} actions :");
        Console.WriteLine();

        for (int i = 0; i < matches.Length; i++)
        {
            ActionSlot slot = matches[i];
            string state = slot.IsBound ? slot.Input!.ToString() : "aucune touche";
            string sense = slot.Need == ActionNeed.Directed ? " [sens explicite]" : string.Empty;

            Console.WriteLine($"    {i + 1,2}. {slot.CommandName,-26} {state,-24} {slot.ActionId}{sense}");
        }

        Console.WriteLine();
        Console.Write("  Numéro (Entrée pour renoncer) : ");

        string? answer = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(answer))
        {
            Console.WriteLine("  Abandon : rien n'a été modifié.");
            return null;
        }

        if (!int.TryParse(answer.Trim(), out int index) || index < 1 || index > matches.Length)
        {
            Console.Error.WriteLine($"  « {answer.Trim()} » n'est pas un numéro de la liste.");
            return null;
        }

        return matches[index - 1];
    }


    /// <summary>
    /// Retire une assignation, rendant à l'action la touche que le jeu lui donnait — ou son
    /// absence de touche.
    ///
    /// Un éditeur qui écrit sans permettre de revenir en arrière est un piège : la capture est
    /// immédiate, une touche pressée par erreur est enregistrée aussitôt, et il faut pouvoir
    /// défaire sans aller éditer un JSON à la main.
    /// </summary>
    public static int Unassign(
        string? target,
        IReadOnlyList<ActionSlot> slots,
        BindingOverlay overlay,
        string overlayPath)
    {
        if (overlay.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Aucune assignation : il n'y a rien à défaire.");
            return 0;
        }

        // On ne cherche que parmi ce qui a été assigné : proposer une action intouchée n'aurait
        // aucun sens ici, et allongerait la liste de choix pour rien.
        ActionSlot[] assigned = slots
            .Where(slot => overlay.Find(slot.ActionId) is not null)
            .ToArray();

        ActionSlot? chosen;

        if (string.IsNullOrWhiteSpace(target))
        {
            chosen = assigned.Length == 1 ? assigned[0] : Choose("vos assignations", assigned);
        }
        else
        {
            ActionSlot[] matches = BindingInventory.Search(assigned, target).ToArray();

            if (matches.Length == 0)
            {
                Console.Error.WriteLine($"Aucune de vos assignations ne correspond à « {target} ».");
                return 1;
            }

            chosen = matches.Length == 1 ? matches[0] : Choose(target, matches);
        }

        if (chosen is null)
        {
            return 1;
        }

        BindingAssignment removed = overlay.Find(chosen.ActionId)!;
        overlay.Remove(chosen.ActionId);
        overlay.Save(overlayPath);

        Console.WriteLine();
        Console.WriteLine($"  retiré     {removed.Input}  de {chosen.ActionId}");
        Console.WriteLine($"  écrit     {overlayPath}");

        PrintGameSideCaveat();
        return 0;
    }

    /// <summary>Efface toutes les assignations, après confirmation écrite.</summary>
    public static int Reset(BindingOverlay overlay, string overlayPath)
    {
        if (overlay.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Aucune assignation : il n'y a rien à effacer.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"  {overlay.Count} assignation(s) vont être effacées :");

        foreach (BindingAssignment assignment in overlay.Assignments)
        {
            Console.WriteLine($"    {assignment.Input,-22} {assignment.ActionId}");
        }

        // Confirmation écrite plutôt qu'un simple o/n : effacer le travail de configuration de
        // quelqu'un mérite mieux qu'une touche pressée par réflexe.
        Console.WriteLine();
        Console.Write("  Tapez EFFACER pour confirmer : ");

        string? answer = Console.ReadLine();

        if (!string.Equals(answer?.Trim(), "EFFACER", StringComparison.Ordinal))
        {
            Console.WriteLine("  Abandon : rien n'a été modifié.");
            return 0;
        }

        foreach (BindingAssignment assignment in overlay.Assignments.ToArray())
        {
            overlay.Remove(assignment.ActionId);
        }

        overlay.Save(overlayPath);

        Console.WriteLine();
        Console.WriteLine($"  effacé    {overlayPath}");

        PrintGameSideCaveat();
        return 0;
    }

    /// <summary>
    /// Rappelle que défaire côté Optimus ne défait rien côté jeu.
    ///
    /// C'est la contrepartie de D35 : puisque assigner demandait deux moitiés, retirer aussi.
    /// Taire cette asymétrie laisserait une touche active dans Star Citizen alors qu'Optimus la
    /// croit libre — exactement le genre d'écart qu'on ne découvre qu'en vol.
    /// </summary>
    private static void PrintGameSideCaveat()
    {
        Console.WriteLine();
        Console.WriteLine("  Attention : Star Citizen garde la touche qu'il a déjà apprise. Optimus ne");
        Console.WriteLine("  l'enverra plus, mais elle reste active si vous la pressez vous-même.");
        Console.WriteLine("  Pour la retirer aussi du jeu : Options > Keybindings, ou rechargez le");
        Console.WriteLine("  profil par défaut depuis Control Profiles.");
    }

    /// <summary>Export de profil le plus récent trouvé dans le dossier du jeu.</summary>
    private static string? NewestLayout(string? mappingsDirectory)
    {
        if (string.IsNullOrWhiteSpace(mappingsDirectory) || !Directory.Exists(mappingsDirectory))
        {
            return null;
        }

        return Directory.EnumerateFiles(mappingsDirectory, "layout_*.xml")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Explique où le jeu range ses profils, et comment en produire un.
    ///
    /// Sans cela, « fichier introuvable » laisse le pilote sans la moindre piste : le dossier
    /// est enfoui, et l'export ne se fait pas au même endroit que les réglages.
    /// </summary>
    private static void PrintWhereLayoutsLive(string? mappingsDirectory)
    {
        Console.WriteLine();

        if (mappingsDirectory is null)
        {
            Console.WriteLine("  Star Citizen n'étant pas lancé, je ne sais pas où il est installé.");
            Console.WriteLine("  Lancez le jeu, ou indiquez le fichier : --import-layout <chemin>");
        }
        else if (!Directory.Exists(mappingsDirectory))
        {
            Console.WriteLine($"  Dossier des profils absent : {mappingsDirectory}");
            Console.WriteLine("  Il n'apparaît qu'après un premier export depuis le jeu.");
        }
        else
        {
            Console.WriteLine($"  Aucun « layout_*.xml » dans {mappingsDirectory}");
        }

        Console.WriteLine();
        Console.WriteLine("  Pour produire un export depuis Star Citizen :");
        Console.WriteLine("    Options > Keybindings > Control Profiles > Save control settings");
        Console.WriteLine("    ou, dans la console (²) : pp_RebindKeys export mesreglages");
        Console.WriteLine();
        Console.WriteLine("  Cet export ne contient que ce que vous avez changé : c'est voulu, et");
        Console.WriteLine("  c'est exactement ce dont Optimus a besoin.");
    }

    /// <summary>Traduit la couche utilisateur en surcharges applicables au profil.</summary>
    public static IReadOnlyList<Binding> ToBindings(BindingOverlay overlay) =>
        overlay.Assignments
            .Select(a => new Binding(a.ActionId, a.Input, UiLabel: null, Unsupported: false))
            .ToList();
}
