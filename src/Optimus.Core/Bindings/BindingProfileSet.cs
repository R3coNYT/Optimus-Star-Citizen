using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;

namespace Optimus.Core.Bindings;

/// <summary>Un profil de touches installé, tel qu'on l'affiche.</summary>
/// <param name="Name">Nom lisible, qui est aussi le nom du fichier.</param>
/// <param name="Path">Fichier d'assignations.</param>
/// <param name="Count">Nombre d'assignations qu'il contient.</param>
public sealed record BindingProfileInfo(string Name, string Path, int Count);

/// <summary>
/// Les profils de touches du pilote.
///
/// Un profil <b>est</b> un jeu d'assignations : le fichier que <see cref="BindingOverlay"/>
/// écrit déjà, simplement nommé. Rien de plus, et c'est délibéré — les touches par défaut du
/// jeu ne changent pas d'un style de vol à l'autre, seul change ce que le pilote a assigné.
/// Faire porter au profil une copie des défauts aurait dupliqué 1103 actions pour en modifier
/// quelques dizaines.
///
/// Pourquoi plusieurs : les touches diffèrent selon ce qu'on pilote. Un chasseur veut ses armes
/// et ses contre-mesures sous les doigts, un mineur ses lasers et son module de fracture, un
/// cargo ses portes et ses treuils. Star Citizen laisse le pilote basculer entre ses propres
/// jeux de touches ; Optimus doit pouvoir suivre sans qu'on lui réassigne tout à la main.
///
/// Les profils vivent dans <c>%APPDATA%\Optimus\bindings</c>, hors de <c>data/</c> que la
/// publication remplace (D35). C'est aussi ce qui les rend copiables d'une machine à l'autre.
/// </summary>
public static class BindingProfileSet
{
    /// <summary>
    /// Nom du profil créé quand le pilote n'en a aucun.
    ///
    /// « starcitizen » était le nom du fichier historique, avant que les profils existent : il
    /// désignait le jeu, pas un style de vol. Le garder comme nom de profil aurait mélangé deux
    /// notions dans la même liste déroulante.
    /// </summary>
    public const string DefaultName = "Standard";

    /// <summary>Dossier des profils.</summary>
    public static string Directory(string? root = null) =>
        root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Optimus", "bindings");

    /// <summary>
    /// Profils présents, par ordre alphabétique.
    ///
    /// Le compte d'assignations est lu au passage : un profil vide se reconnaît d'un coup d'œil,
    /// et c'est souvent le signe qu'on a basculé sans avoir encore rien importé.
    /// </summary>
    public static IReadOnlyList<BindingProfileInfo> List(string? root = null)
    {
        string directory = Directory(root);

        if (!System.IO.Directory.Exists(directory))
        {
            return Array.Empty<BindingProfileInfo>();
        }

        List<BindingProfileInfo> profiles = new();

        foreach (string file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            profiles.Add(new BindingProfileInfo(
                Path.GetFileNameWithoutExtension(file),
                file,
                BindingOverlay.Load(file).Count));
        }

        return profiles
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Chemin du fichier d'un profil, existant ou non.</summary>
    public static string PathOf(string name, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return Path.Combine(Directory(root), Sanitize(name) + ".json");
    }

    /// <summary>Vrai si un profil de ce nom existe déjà, à la casse près.</summary>
    public static bool Exists(string name, string? root = null) =>
        !string.IsNullOrWhiteSpace(name)
        && List(root).Any(p => string.Equals(p.Name, Sanitize(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Nom du profil à activer au démarrage.
    ///
    /// Le profil enregistré s'il existe encore, le premier venu sinon, et
    /// <see cref="DefaultName"/> si le pilote n'en a aucun. Cette dernière branche compte :
    /// c'est elle qui évite qu'un premier lancement se retrouve sans profil du tout, à
    /// enregistrer des assignations dans le vide.
    /// </summary>
    public static string Resolve(string? preferred, string? root = null)
    {
        IReadOnlyList<BindingProfileInfo> profiles = List(root);

        if (profiles.Count == 0)
        {
            return DefaultName;
        }

        BindingProfileInfo? wanted = preferred is null
            ? null
            : profiles.FirstOrDefault(
                p => string.Equals(p.Name, preferred, StringComparison.OrdinalIgnoreCase));

        return (wanted ?? profiles[0]).Name;
    }

    /// <summary>
    /// Crée un profil, éventuellement en copiant un autre.
    ///
    /// La duplication est le geste utile : un profil « Minage » part presque toujours du profil
    /// de vol habituel, dont on ne change ensuite qu'une poignée de touches. Repartir de zéro
    /// obligerait à tout réassigner pour n'en modifier que dix.
    /// </summary>
    /// <returns>Le chemin du profil créé.</returns>
    public static string Create(string name, string? copyFrom = null, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string directory = Directory(root);
        System.IO.Directory.CreateDirectory(directory);

        string target = PathOf(name, root);

        if (File.Exists(target))
        {
            throw new InvalidOperationException($"Un profil « {Sanitize(name)} » existe déjà.");
        }

        BindingOverlay overlay = copyFrom is null
            ? new BindingOverlay()
            : BindingOverlay.Load(PathOf(copyFrom, root));

        overlay.Save(target);

        return target;
    }

    /// <summary>Renomme un profil. Retourne le nouveau chemin.</summary>
    public static string Rename(string from, string to, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        string source = PathOf(from, root);
        string target = PathOf(to, root);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        if (File.Exists(target))
        {
            throw new InvalidOperationException($"Un profil « {Sanitize(to)} » existe déjà.");
        }

        if (!File.Exists(source))
        {
            throw new InvalidOperationException($"Le profil « {from} » n'existe pas.");
        }

        File.Move(source, target);

        return target;
    }

    /// <summary>
    /// Supprime un profil.
    ///
    /// Le dernier ne se supprime pas : sans profil, les assignations du pilote n'auraient plus
    /// où aller, et l'écran des touches deviendrait un formulaire qui n'enregistre rien.
    /// </summary>
    public static void Delete(string name, string? root = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (List(root).Count <= 1)
        {
            throw new InvalidOperationException(
                "C'est le dernier profil : vos assignations n'auraient plus où être enregistrées.");
        }

        string path = PathOf(name, root);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Réduit un nom à ce qu'un système de fichiers accepte.
    ///
    /// Nettoyer plutôt que refuser : un pilote qui nomme son profil « Chasse / Escorte » a écrit
    /// quelque chose de parfaitement sensé, et lui opposer une règle de nommage serait faire
    /// porter à l'utilisateur une contrainte technique. Les accents sont conservés — le nom
    /// s'affiche, et « Général » vaut mieux que « General ».
    /// </summary>
    public static string Sanitize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        char[] forbidden = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Trim().Select(c => forbidden.Contains(c) ? '-' : c).ToArray());

        // Un nom vide apres nettoyage rendrait un chemin ".json", invisible et inexploitable.
        return cleaned.Length == 0 ? DefaultName : cleaned;
    }

    /// <summary>
    /// Formulations qui déclenchent la bascule vers ce profil, à la voix.
    ///
    /// C'est là tout l'intérêt : changer de vaisseau se fait au hangar, mais changer de style de
    /// vol se fait en vol. Basculer sans quitter le jeu est exactement ce qu'on attend d'un
    /// copilote — alt-tabber pour cliquer dans une liste déroulante annulerait le bénéfice.
    /// </summary>
    public static IReadOnlyList<string> Phrases(string name, string? language = null) =>
        Localization.GeneratedPhrases.ForBindingProfile(name, language);

    /// <summary>Prefixe des commandes de bascule, pour les reconnaitre a l'execution.</summary>
    public const string CommandPrefix = "bindings.profile.";

    /// <summary>Cle de reponse employee quand Optimus confirme une bascule.</summary>
    public const string ResponseKey = "bindings.profile";

    /// <summary>Identifiant de commande d'une bascule de profil.</summary>
    public static string CommandId(string name) =>
        CommandPrefix + TextNormalizer.Normalize(name).Replace(' ', '_');

    /// <summary>
    /// Commandes vocales de bascule, une par profil.
    ///
    /// Elles sont <b>passives</b> — aucune touche n'est envoyee au jeu — et c'est essentiel :
    /// une commande active serait soumise a la garde, qui exige que Star Citizen soit au premier
    /// plan. Changer de profil depuis le bureau, entre deux sessions, deviendrait impossible
    /// pour une raison que rien n'expliquerait.
    ///
    /// Le nom du profil voyage dans <see cref="CommandDefinition.Description"/> : l'identifiant
    /// est normalise, donc irreversible, et le retrouver par recherche inverse serait fragile
    /// le jour ou deux profils normaliseraient pareil.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Commands(
        IEnumerable<BindingProfileInfo> profiles, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        List<CommandDefinition> commands = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (BindingProfileInfo profile in profiles)
        {
            string id = CommandId(profile.Name);

            // Deux profils qui se normalisent pareil - « Minage » et « minage » - donneraient
            // deux commandes de meme identifiant, et la grammaire n'en garderait qu'une. Mieux
            // vaut n'en declarer qu'une que d'en rendre une inatteignable en silence.
            if (!seen.Add(id))
            {
                continue;
            }

            commands.Add(new CommandDefinition(
                id,
                CommandKind.Query,
                Localization.GeneratedPhrases.BindingProfileName(profile.Name, language),
                Localization.GeneratedPhrases.BindingProfileCategory(language),
                Phrases(profile.Name, language),
                Array.Empty<ActionStep>(),
                Description: profile.Name));
        }

        return commands;
    }

    /// <summary>
    /// Ajoute a un catalogue les commandes de bascule des profils installes.
    ///
    /// Partagee par l'application et le banc d'essai : deux fusions separees finiraient par
    /// diverger, et le banc dirait alors qu'une formulation n'est pas comprise la ou
    /// l'application l'execute - ou l'inverse, ce qui est pire.
    /// </summary>
    public static CommandCatalog Augment(
        CommandCatalog catalog, string? root = null, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        IReadOnlyList<CommandDefinition> commands = Commands(List(root), language);

        return commands.Count == 0
            ? catalog
            : CommandCatalog.Merge(
                catalog.Id, catalog.Name, catalog,
                new CommandCatalog("profils", "Profils de touches", commands));
    }

    /// <summary>Nom du profil vise par une commande de bascule, ou <c>null</c>.</summary>
    public static string? ProfileOf(CommandDefinition? command) =>
        command is not null
        && command.Id.StartsWith(CommandPrefix, StringComparison.Ordinal)
            ? command.Description
            : null;
}
