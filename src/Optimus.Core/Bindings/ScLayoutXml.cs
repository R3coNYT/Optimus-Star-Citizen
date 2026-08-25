using System.Xml.Linq;
using Optimus.Core.Domain.Bindings;

namespace Optimus.Core.Bindings;

/// <summary>Une assignation lue dans un fichier de mappage du jeu, ou destinée à y être écrite.</summary>
/// <param name="ActionId">Identifiant <c>actionmap/action</c>.</param>
/// <param name="Input">Entrée physique.</param>
public sealed record LayoutEntry(string ActionId, InputSpec Input);

/// <summary>Bilan d'une lecture de fichier de mappage.</summary>
/// <param name="Entries">Assignations exploitables.</param>
/// <param name="Skipped">Assignations ignorées, avec leur raison — jamais tues.</param>
/// <param name="LayoutName">Nom du profil, tel que le jeu l'affiche.</param>
public sealed record LayoutImport(
    IReadOnlyList<LayoutEntry> Entries,
    IReadOnlyList<string> Skipped,
    string? LayoutName);

/// <summary>
/// Lecture et écriture des fichiers de mappage de Star Citizen.
///
/// Le jeu range ses profils dans <c>USER\Client\0\Controls\Mappings\*.xml</c> et les charge
/// depuis les options ou par <c>pp_RebindKeys</c>. Ce sont de simples <c>&lt;ActionMaps&gt;</c>
/// ne contenant que les <b>écarts</b> au profil par défaut — d'où le modèle « défauts ⊕ deltas »
/// retenu dès la conception.
///
/// Les deux sens comptent autant l'un que l'autre :
/// <list type="bullet">
/// <item>en lecture, Optimus apprend les touches que le pilote a lui-même changées ;</item>
/// <item>en écriture, il produit un fichier que le jeu peut relire — sans quoi assigner une
/// touche côté Optimus ne ferait rien du tout, puisque Star Citizen n'obéit qu'à ses propres
/// bindings.</item>
/// </list>
/// </summary>
public static class ScLayoutXml
{
    /// <summary>Lit un fichier de mappage exporté par le jeu.</summary>
    public static LayoutImport Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(XDocument.Load(path));
    }

    /// <summary>Lit un mappage déjà chargé en mémoire.</summary>
    public static LayoutImport Parse(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<LayoutEntry> entries = new();
        List<string> skipped = new();

        XElement? root = document.Root;
        if (root is null)
        {
            return new LayoutImport(entries, ["fichier vide"], null);
        }

        string? layoutName = root.Element("CustomisationUIHeader")?.Attribute("label")?.Value
            ?? root.Attribute("profileName")?.Value;

        foreach (XElement map in root.Elements("actionmap"))
        {
            string? mapName = map.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(mapName))
            {
                continue;
            }

            foreach (XElement action in map.Elements("action"))
            {
                string? actionName = action.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(actionName))
                {
                    continue;
                }

                string actionId = $"{mapName}/{actionName}";

                // Une entrée peut vivre dans « keyboard », dans « mouse », ou dans les deux ;
                // et « rebind » imbriqué sert quand le jeu écrit plusieurs périphériques.
                string? raw = action.Attribute("keyboard")?.Value
                    ?? action.Attribute("mouse")?.Value
                    ?? action.Elements("rebind")
                        .Select(r => r.Attribute("input")?.Value)
                        .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

                // Le jeu écrit une chaîne vide pour dire « cette action n'a plus de touche ».
                // C'est une information, pas un défaut : elle défait un défaut du jeu.
                if (string.IsNullOrWhiteSpace(raw) || raw == " ")
                {
                    skipped.Add($"{actionId} : touche retirée par le pilote");
                    continue;
                }

                // Les préfixes de périphérique (« kb1_ », « mo1_ ») n'apportent rien ici.
                string cleaned = StripDevicePrefix(raw);

                if (ScKeyNames.Parse(cleaned) is not InputSpec input)
                {
                    skipped.Add($"{actionId} : « {raw} » non injectable");
                    continue;
                }

                entries.Add(new LayoutEntry(actionId, input));
            }
        }

        return new LayoutImport(entries, skipped, layoutName);
    }

    /// <summary>
    /// Écrit un fichier de mappage que Star Citizen sait relire.
    ///
    /// Ne contient que les actions passées en argument : c'est un <b>delta</b>, exactement comme
    /// les exports du jeu. Tout ce qui n'y figure pas garde sa touche par défaut.
    /// </summary>
    public static XDocument Write(IEnumerable<LayoutEntry> entries, string layoutName)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutName);

        XElement root = new(
            "ActionMaps",
            new XAttribute("version", "1"),
            new XAttribute("optionsVersion", "2"),
            new XAttribute("rebindVersion", "2"),
            new XAttribute("profileName", layoutName),
            new XElement(
                "CustomisationUIHeader",
                new XAttribute("label", layoutName),
                new XAttribute("description", "Assignations demandées par Optimus"),
                new XElement("devices", new XElement("keyboard", new XAttribute("instance", "1")))),
            new XElement("options", new XAttribute("type", "keyboard"), new XAttribute("instance", "1")),
            new XElement("modifiers"));

        foreach (IGrouping<string, LayoutEntry> group in entries
            .GroupBy(e => e.ActionId.Split('/')[0], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            XElement map = new("actionmap", new XAttribute("name", group.Key));

            foreach (LayoutEntry entry in group.OrderBy(e => e.ActionId, StringComparer.Ordinal))
            {
                string? formatted = ScKeyNames.Format(entry.Input);
                if (formatted is null)
                {
                    continue;
                }

                string actionName = entry.ActionId[(entry.ActionId.IndexOf('/') + 1)..];

                // Forme des profils utilisateur : un element « rebind » et rien d'autre.
                // L'attribut « keyboard= » appartient a defaultProfile.xml, que le jeu lit par
                // un autre chemin - melanger les deux formes serait un pari inutile.
                map.Add(new XElement(
                    "action",
                    new XAttribute("name", actionName),
                    new XElement("rebind", new XAttribute("input", $"{Prefix(entry.Input)}{formatted}"))));
            }

            if (map.HasElements)
            {
                root.Add(map);
            }
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    /// <summary>
    /// Enregistre un mappage sans marque d'ordre d'octets.
    ///
    /// Les fichiers que le jeu produit n'en portent pas ; rien ne prouve qu'il refuserait un
    /// BOM, mais rien ne prouve l'inverse non plus, et ce fichier n'a qu'une seule occasion
    /// d'etre accepte.
    /// </summary>
    public static void Save(XDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using StreamWriter writer = new(path, false, new System.Text.UTF8Encoding(false));
        document.Save(writer);
    }

    private static string Prefix(InputSpec input) =>
        input.Device == InputDevice.Mouse ? "mo1_" : "kb1_";

    private static string StripDevicePrefix(string raw)
    {
        string[] prefixes = ["kb1_", "kb2_", "mo1_", "mo2_", "js1_", "gp1_"];

        foreach (string prefix in prefixes)
        {
            if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return raw[prefix.Length..];
            }
        }

        return raw;
    }
}
