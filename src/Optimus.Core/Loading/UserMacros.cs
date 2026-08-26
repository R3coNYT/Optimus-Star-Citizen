using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Loading;

/// <summary>
/// Les macros écrites par le pilote, rangées hors du catalogue livré.
///
/// Même raison que pour les assignations de touches : le script de publication recopie tout
/// <c>data/</c>, et une macro écrite dans le catalogue livré serait effacée à la première mise à
/// jour. Elles vivent donc dans les données de l'utilisateur et se <b>superposent</b> au
/// catalogue — une macro qui porte l'identifiant d'une macro livrée la remplace, les autres
/// s'ajoutent.
///
/// Le fichier a exactement la forme d'un catalogue, et c'est délibéré : il se relit avec le même
/// <see cref="JsonCatalogLoader"/>, se valide avec le même outillage, et reste modifiable à la
/// main par qui préfère un éditeur de texte à une fenêtre.
/// </summary>
public static class UserMacros
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,

        // Les noeuds construits a la main - JsonArray.Add, l'indexeur - sont enveloppes dans un
        // JsonValue generique, que la serialisation refuse d'ecrire sans resolveur de types.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),

        // Sans cela les accents ressortent en séquences échappées, illisibles dans un fichier
        // que l'utilisateur a le droit d'ouvrir.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Emplacement par défaut, dans les données de l'utilisateur.</summary>
    public static string DefaultPath(string profileId = "starcitizen") =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Optimus", "macros", $"{profileId}.json");

    /// <summary>
    /// Charge les macros du pilote. Retourne un catalogue vide si le fichier n'existe pas encore
    /// — le cas normal tant qu'il n'en a écrit aucune.
    /// </summary>
    public static LoadResult<CommandCatalog> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.Exists(path)
            ? JsonCatalogLoader.LoadCatalog(path)
            : new LoadResult<CommandCatalog>(CommandCatalog.Empty, Array.Empty<LoadIssue>());
    }

    /// <summary>Écrit les macros du pilote, en créant le dossier au besoin.</summary>
    public static void Save(string path, IEnumerable<CommandDefinition> macros)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(macros);

        JsonArray commands = new();

        foreach (CommandDefinition macro in macros.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            commands.Add(Serialize(macro));
        }

        JsonObject root = new()
        {
            ["$schema"] = "optimus://schemas/commandset-1.json",
            ["id"] = "user.macros",
            ["name"] = "Macros du pilote",
            ["note"] = "Écrit par Optimus. Ce fichier se superpose au catalogue livré : une macro "
                     + "qui en porte l'identifiant le remplace. Modifiable à la main.",
            ["commands"] = commands,
        };

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Fichier temporaire puis remplacement : une coupure au mauvais moment laisserait sinon
        // un JSON tronque, et Optimus refuserait de demarrer avec pour seul indice une erreur
        // d'analyse.
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(Format));
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonObject Serialize(CommandDefinition macro)
    {
        JsonObject node = new()
        {
            ["id"] = macro.Id,
            ["kind"] = "macro",
            ["name"] = macro.Name,
            ["category"] = macro.Category,
            ["voice_phrases"] = Strings(macro.VoicePhrases),
            ["actions"] = Steps(macro.Actions),
        };

        if (macro.CooldownMs > 0)
        {
            node["cooldown_ms"] = macro.CooldownMs;
        }

        return node;
    }

    private static JsonArray Strings(IEnumerable<string> values)
    {
        JsonArray array = new();

        foreach (string value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonArray Steps(IEnumerable<ActionStep> steps)
    {
        JsonArray array = new();

        foreach (ActionStep step in steps)
        {
            array.Add(Serialize(step));
        }

        return array;
    }

    private static JsonObject Serialize(ActionStep step) => step.Type switch
    {
        ActionStepType.Wait => new JsonObject { ["type"] = "wait", ["ms"] = step.WaitMs },

        ActionStepType.Say => new JsonObject
        {
            ["type"] = "say",
            ["response_key"] = step.ResponseKey,
        },

        ActionStepType.Command => Command(step),

        ActionStepType.If => Branch(step),

        ActionStepType.Repeat => new JsonObject
        {
            ["type"] = "repeat",
            ["times"] = step.Repeat,
            ["body"] = Steps(step.Block),
        },

        _ => new JsonObject { ["type"] = "game_action", ["action_id"] = step.ActionId },
    };

    private static JsonObject Branch(ActionStep step)
    {
        JsonObject node = new()
        {
            ["type"] = "if",
            ["condition"] = Condition(step.Condition),
            ["then"] = Steps(step.Block),
        };

        // Un « sinon » vide ne s'ecrit pas : le fichier se relit a la main, et une cle vide y
        // ferait chercher une intention qui n'y est pas.
        if (step.Alternative.Count > 0)
        {
            node["else"] = Steps(step.Alternative);
        }

        return node;
    }

    private static JsonObject Condition(MacroCondition? condition)
    {
        if (condition is null)
        {
            return new JsonObject();
        }

        JsonObject node = new()
        {
            ["subject"] = condition.Subject switch
            {
                ConditionSubject.Binding => "binding",
                ConditionSubject.Directed => "directed",
                ConditionSubject.Simulation => "simulation",
                ConditionSubject.FlightMode => "flight_mode",
                _ => "believed",
            },
        };

        if (condition.CommandId is not null)
        {
            node["command_id"] = condition.CommandId;
        }

        if (condition.Polarity != CommandPolarity.Neutral)
        {
            node["polarity"] = condition.Polarity == CommandPolarity.On ? "on" : "off";
        }

        if (condition.Value is not null)
        {
            node["value"] = condition.Value;
        }

        if (condition.Negated)
        {
            node["negated"] = true;
        }

        return node;
    }

    private static JsonObject Command(ActionStep step)
    {
        JsonObject node = new()
        {
            ["type"] = "command",
            ["command_id"] = step.CommandId,
        };

        if (step.Polarity != CommandPolarity.Neutral)
        {
            node["polarity"] = step.Polarity == CommandPolarity.On ? "on" : "off";
        }

        if (step.RequireDirected)
        {
            node["require_directed"] = true;
        }

        return node;
    }
}
