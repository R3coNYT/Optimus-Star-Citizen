using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;

namespace Optimus.Core.Loading;

/// <summary>Un copilote installé, tel qu'on le présente au pilote.</summary>
/// <param name="Id">Nom du dossier, qui sert d'identifiant.</param>
/// <param name="Name">Nom prononcé.</param>
/// <param name="WakeWord">Mot d'éveil qui lui est propre.</param>
/// <param name="Directory">Dossier d'où il est chargé.</param>
/// <param name="IsUsers">Vrai s'il appartient au pilote, faux s'il est livré avec Optimus.</param>
public sealed record CopilotInfo(
    string Id,
    string Name,
    string WakeWord,
    string Directory,
    bool IsUsers);

/// <summary>
/// Les copilotes disponibles.
///
/// Le cahier des charges le dit depuis le début (§7) : Optimus, Synthia ou Virgil ne sont pas
/// trois programmes mais <b>trois jeux de données</b>. Ce qui manquait n'était donc pas le
/// modèle — il était là — mais de quoi en avoir plusieurs et passer de l'un à l'autre.
///
/// Deux emplacements, et la même règle que les macros (D43) et les formulations (D46) : ceux
/// qui sont livrés vivent dans <c>data/copilots</c>, que la publication remplace ; ceux du
/// pilote vivent dans <c>%APPDATA%\Optimus\copilots</c>, où rien ne les efface. Un copilote du
/// pilote portant l'identifiant d'un copilote livré le <b>masque</b> sans le détruire :
/// supprimer la copie restitue l'original au lieu de le perdre.
/// </summary>
public static class CopilotSet
{
    /// <summary>Dossier des copilotes du pilote.</summary>
    public static string UserDirectory(string? root = null) =>
        root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Optimus", "copilots");

    /// <summary>Dossier des copilotes livrés.</summary>
    public static string ShippedDirectory(string dataRoot) =>
        Path.Combine(dataRoot, "data", "copilots");

    /// <summary>
    /// Copilotes disponibles, ceux du pilote masquant ceux qui sont livrés.
    ///
    /// Un dossier sans manifeste lisible est ignoré : mieux vaut un copilote absent de la liste
    /// qu'une liste qui propose un choix menant à un écran d'erreur.
    /// </summary>
    public static IReadOnlyList<CopilotInfo> List(string dataRoot, string? userRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        Dictionary<string, CopilotInfo> found = new(StringComparer.OrdinalIgnoreCase);

        foreach (string directory in Directories(ShippedDirectory(dataRoot)))
        {
            Add(found, directory, isUsers: false);
        }

        foreach (string directory in Directories(UserDirectory(userRoot)))
        {
            Add(found, directory, isUsers: true);
        }

        return found.Values
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Dossier d'où charger un copilote, ou <c>null</c> s'il n'existe nulle part.</summary>
    public static string? DirectoryOf(string id, string dataRoot, string? userRoot = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        string mine = Path.Combine(UserDirectory(userRoot), id);

        if (File.Exists(Path.Combine(mine, "copilot.json")))
        {
            return mine;
        }

        string shipped = Path.Combine(ShippedDirectory(dataRoot), id);

        return File.Exists(Path.Combine(shipped, "copilot.json")) ? shipped : null;
    }

    /// <summary>
    /// Identifiant du copilote à charger au démarrage.
    ///
    /// Celui qui est enregistré s'il existe encore, le premier venu sinon. Un pilote qui
    /// supprime son copilote favori doit retrouver Optimus qui parle, pas un écran muet.
    /// </summary>
    public static string Resolve(string? preferred, string dataRoot, string? userRoot = null)
    {
        IReadOnlyList<CopilotInfo> copilots = List(dataRoot, userRoot);

        if (copilots.Count == 0)
        {
            return preferred ?? "optimus";
        }

        CopilotInfo? wanted = preferred is null
            ? null
            : copilots.FirstOrDefault(
                c => string.Equals(c.Id, preferred, StringComparison.OrdinalIgnoreCase));

        return (wanted ?? copilots[0]).Id;
    }

    /// <summary>
    /// Crée un copilote en copiant un existant, dans le dossier du pilote.
    ///
    /// Toujours par copie, jamais depuis rien : un copilote sans répliques est un copilote muet,
    /// et repartir de zéro obligerait à réécrire soixante-cinq entrées de dialogue pour changer
    /// un nom et deux curseurs. Le geste utile est « comme Optimus, mais plus laconique ».
    /// </summary>
    /// <returns>Le dossier créé.</returns>
    public static string Create(
        string id, string name, string copyFrom, string dataRoot, string? userRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(copyFrom);

        string clean = Sanitize(id);
        string target = Path.Combine(UserDirectory(userRoot), clean);

        if (Directory.Exists(target))
        {
            throw new InvalidOperationException($"Un copilote « {clean} » vous appartient déjà.");
        }

        string source = DirectoryOf(copyFrom, dataRoot, userRoot)
            ?? throw new InvalidOperationException($"Le copilote « {copyFrom} » est introuvable.");

        Directory.CreateDirectory(target);

        foreach (string file in Directory.EnumerateFiles(source, "*.json"))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        // Le manifeste porte l'identite : la copie doit prendre la sienne, sans quoi deux
        // copilotes se presenteraient sous le meme nom et repondraient au meme mot d'eveil.
        SettingsWriter.SaveCopilotIdentity(Path.Combine(target, "copilot.json"), clean, name);

        return target;
    }

    /// <summary>
    /// Supprime un copilote du pilote.
    ///
    /// Les copilotes livrés ne se suppriment pas — ils reviendraient à la publication suivante,
    /// et prétendre le contraire serait mentir. Supprimer une copie qui masquait un copilote
    /// livré <b>restitue l'original</b> : c'est ce qui rend l'essai sans risque.
    /// </summary>
    public static void Delete(string id, string? userRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        string mine = Path.Combine(UserDirectory(userRoot), id);

        if (!Directory.Exists(mine))
        {
            throw new InvalidOperationException(
                $"« {id} » est livré avec Optimus : il ne peut pas être supprimé, "
                + "seulement masqué par une copie à vous.");
        }

        Directory.Delete(mine, recursive: true);
    }

    /// <summary>Réduit un identifiant à ce qu'un dossier accepte, en minuscules.</summary>
    public static string Sanitize(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        char[] forbidden = Path.GetInvalidFileNameChars();

        string cleaned = new(TextNormalizer.Normalize(id)
            .Select(c => forbidden.Contains(c) || c == ' ' ? '-' : c)
            .ToArray());

        return cleaned.Trim('-').Length == 0 ? "copilote" : cleaned.Trim('-');
    }

    // ------------------------------------------------------------------ la bascule a la voix

    /// <summary>Préfixe des commandes de bascule, pour les reconnaître à l'exécution.</summary>
    public const string CommandPrefix = "copilot.switch.";

    /// <summary>Clé de réplique employée quand un copilote passe la main.</summary>
    public const string ResponseKey = "copilot.switch";

    /// <summary>Identifiant de commande d'une bascule vers ce copilote.</summary>
    public static string CommandId(string id) => CommandPrefix + Sanitize(id);

    /// <summary>
    /// Commandes de bascule, une par copilote.
    ///
    /// Passives : aucune touche n'est envoyée au jeu, et la garde n'a donc pas à exiger que
    /// Star Citizen soit au premier plan. Changer de copilote depuis le bureau doit rester
    /// possible.
    ///
    /// <b>Le copilote actif n'a pas la sienne.</b> « Passe à Optimus » alors qu'Optimus répond
    /// déjà n'apporte rien, et occuperait une formulation dans la grammaire pour ne rien faire.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Commands(
        IEnumerable<CopilotInfo> copilots, string activeId)
    {
        ArgumentNullException.ThrowIfNull(copilots);

        List<CommandDefinition> commands = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (CopilotInfo copilot in copilots)
        {
            if (string.Equals(copilot.Id, activeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string command = CommandId(copilot.Id);

            if (!seen.Add(command))
            {
                continue;
            }

            commands.Add(new CommandDefinition(
                command,
                CommandKind.Query,
                copilot.Name,
                "copilotes",
                [
                    $"passe à {copilot.Name}",
                    $"appelle {copilot.Name}",
                    $"copilote {copilot.Name}",
                ],
                Array.Empty<ActionStep>(),
                Description: copilot.Id));
        }

        return commands;
    }

    /// <summary>Copilote visé par une commande de bascule, ou <c>null</c>.</summary>
    public static string? TargetOf(CommandDefinition? command) =>
        command is not null
        && command.Id.StartsWith(CommandPrefix, StringComparison.Ordinal)
            ? command.Description
            : null;

    // ------------------------------------------------------------------------------ interne

    private static IEnumerable<string> Directories(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateDirectories(root)
            : Array.Empty<string>();

    private static void Add(Dictionary<string, CopilotInfo> found, string directory, bool isUsers)
    {
        string manifest = Path.Combine(directory, "copilot.json");

        if (!File.Exists(manifest))
        {
            return;
        }

        LoadResult<Domain.Copilots.Copilot> loaded = CopilotLoader.Load(directory);

        if (loaded.Issues.Any(i => i.Message.Contains("introuvable", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        string id = Path.GetFileName(directory);

        found[id] = new CopilotInfo(
            id, loaded.Value.Name, loaded.Value.WakeWord, directory, isUsers);
    }
}
