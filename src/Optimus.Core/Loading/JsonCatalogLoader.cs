using System.Text.Json;
using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Loading;

/// <summary>Problème rencontré au chargement d'un fichier de configuration.</summary>
/// <param name="Path">Fichier concerné.</param>
/// <param name="Element">Élément fautif, quand il est identifiable.</param>
/// <param name="Message">Description du problème.</param>
public sealed record LoadIssue(string Path, string? Element, string Message)
{
    public override string ToString() =>
        Element is null ? $"{Path} : {Message}" : $"{Path} [{Element}] : {Message}";
}

/// <summary>Résultat d'un chargement : ce qui a été lu, et ce qui a été écarté.</summary>
public sealed record LoadResult<T>(T Value, IReadOnlyList<LoadIssue> Issues)
{
    public bool HasIssues => Issues.Count > 0;
}

/// <summary>
/// Lecture des catalogues et profils de binding au format JSON.
///
/// Principe de robustesse : un élément invalide est écarté et signalé, il n'empêche jamais le
/// démarrage. Une faute de frappe dans une commande ne doit pas priver l'utilisateur des
/// cinquante-huit autres — ni le laisser sans explication.
/// </summary>
public static class JsonCatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Charge un catalogue de commandes.</summary>
    public static LoadResult<CommandCatalog> LoadCatalog(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<LoadIssue> issues = new();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });

        JsonElement root = document.RootElement;
        string id = GetString(root, "id") ?? Path.GetFileNameWithoutExtension(path);
        string name = GetString(root, "name") ?? id;

        List<CommandDefinition> commands = new();

        if (!root.TryGetProperty("commands", out JsonElement commandsElement) ||
            commandsElement.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new LoadIssue(path, null, "Aucun tableau « commands »."));
            return new LoadResult<CommandCatalog>(new CommandCatalog(id, name, commands), issues);
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement element in commandsElement.EnumerateArray())
        {
            string? commandId = GetString(element, "id");
            if (string.IsNullOrWhiteSpace(commandId))
            {
                issues.Add(new LoadIssue(path, null, "Commande sans identifiant, ignorée."));
                continue;
            }

            if (!seen.Add(commandId))
            {
                issues.Add(new LoadIssue(path, commandId, "Identifiant en double, commande ignorée."));
                continue;
            }

            if (!TryParseKind(GetString(element, "kind"), out CommandKind kind))
            {
                issues.Add(new LoadIssue(path, commandId, $"Type « {GetString(element, "kind")} » inconnu, commande ignorée."));
                continue;
            }

            List<string> phrases = GetStringArray(element, "voice_phrases");
            if (phrases.Count == 0)
            {
                issues.Add(new LoadIssue(path, commandId, "Aucune phrase vocale, commande ignorée."));
                continue;
            }

            List<ActionStep> steps = ParseActions(element, path, commandId, issues);

            // Sens explicites. Le jeu declare des actions dirigees pour une partie des bascules
            // (v_lights_on / v_lights_off) sans leur assigner de touche ; les declarer ici les
            // rend utilisables des que l'editeur de keybinds en configure une.
            List<string> phrasesOn = GetStringArray(element, "phrases_on");
            List<string> phrasesOff = GetStringArray(element, "phrases_off");
            List<ActionStep> stepsOn = ParseActions(element, path, commandId, issues, "actions_on");
            List<ActionStep> stepsOff = ParseActions(element, path, commandId, issues, "actions_off");

            foreach (string duplicate in phrasesOn.Intersect(phrasesOff, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new LoadIssue(path, commandId,
                    $"La phrase « {duplicate} » demande a la fois l'activation et l'extinction."));
            }

            if (kind == CommandKind.Action && steps.Count == 0)
            {
                issues.Add(new LoadIssue(path, commandId, "Commande d'action sans étape exécutable, ignorée."));
                continue;
            }

            commands.Add(new CommandDefinition(
                Id: commandId,
                Kind: kind,
                Name: GetString(element, "name") ?? commandId,
                Category: GetString(element, "category") ?? "system",
                VoicePhrases: phrases,
                Actions: steps,
                CooldownMs: GetInt(element, "cooldown_ms") ?? 0,
                Dangerous: GetBool(element, "dangerous") ?? false,
                Description: GetString(element, "description"),
                Source: GetString(element, "source") ?? "builtin")
            {
                PhrasesOn = phrasesOn,
                PhrasesOff = phrasesOff,
                ActionsOn = stepsOn,
                ActionsOff = stepsOff,
            });
        }

        return new LoadResult<CommandCatalog>(new CommandCatalog(id, name, commands), issues);
    }

    /// <summary>Charge un profil de binding produit par <c>tools/convert-default-profile.ps1</c>.</summary>
    public static LoadResult<BindingProfile> LoadBindingProfile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<LoadIssue> issues = new();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        string id = GetString(root, "id") ?? Path.GetFileNameWithoutExtension(path);
        string name = GetString(root, "name") ?? id;
        string gameVersion = GetString(root, "game_version") ?? "inconnue";
        string? gameBuild = GetString(root, "game_build");

        List<Binding> bindings = new();

        if (root.TryGetProperty("bindings", out JsonElement bindingsElement) &&
            bindingsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in bindingsElement.EnumerateObject())
            {
                string? key = GetString(property.Value, "key");
                if (string.IsNullOrWhiteSpace(key))
                {
                    issues.Add(new LoadIssue(path, property.Name, "Binding sans touche, ignoré."));
                    continue;
                }

                InputSpec input = new(
                    Key: key,
                    Modifiers: GetStringArray(property.Value, "mods"),
                    Device: ParseDevice(GetString(property.Value, "device")),
                    Mode: ParseMode(GetString(property.Value, "mode")),
                    HoldMs: GetInt(property.Value, "hold_ms") ?? InputSpec.DefaultHoldMs);

                bindings.Add(new Binding(
                    ActionId: property.Name,
                    Input: input,
                    UiLabel: GetString(property.Value, "ui_label"),
                    Unsupported: GetBool(property.Value, "unsupported") ?? false));
            }
        }
        else
        {
            issues.Add(new LoadIssue(path, null, "Aucun objet « bindings »."));
        }

        List<string> unbound = GetStringArray(root, "unbound");

        return new LoadResult<BindingProfile>(
            new BindingProfile(id, name, gameVersion, gameBuild, bindings, unbound),
            issues);
    }

    private static List<ActionStep> ParseActions(
        JsonElement command, string path, string commandId, List<LoadIssue> issues,
        string property = "actions")
    {
        List<ActionStep> steps = new();

        if (!command.TryGetProperty(property, out JsonElement actions) ||
            actions.ValueKind != JsonValueKind.Array)
        {
            return steps;
        }

        foreach (JsonElement action in actions.EnumerateArray())
        {
            string type = GetString(action, "type") ?? "game_action";

            switch (type)
            {
                case "game_action":
                    {
                        string? actionId = GetString(action, "action_id");
                        if (string.IsNullOrWhiteSpace(actionId))
                        {
                            issues.Add(new LoadIssue(path, commandId, "Étape « game_action » sans action_id, ignorée."));
                            continue;
                        }

                        steps.Add(new ActionStep(
                            Type: ActionStepType.GameAction,
                            ActionId: actionId,
                            Mode: TryParseMode(GetString(action, "mode")),
                            HoldMs: GetInt(action, "hold_ms"),
                            Repeat: GetInt(action, "repeat") ?? 1,
                            IntervalMs: GetInt(action, "interval_ms") ?? InputSpec.DefaultIntervalMs));
                        break;
                    }

                case "wait":
                    steps.Add(ActionStep.Wait(GetInt(action, "ms") ?? 0));
                    break;

                case "say":
                    steps.Add(new ActionStep(ActionStepType.Say, ResponseKey: GetString(action, "response_key")));
                    break;

                default:
                    issues.Add(new LoadIssue(path, commandId, $"Type d'étape « {type} » non pris en charge, ignorée."));
                    break;
            }
        }

        return steps;
    }

    private static bool TryParseKind(string? value, out CommandKind kind)
    {
        kind = CommandKind.Action;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out kind);
    }

    private static InputMode ParseMode(string? value) => TryParseMode(value) ?? InputMode.Tap;

    private static InputMode? TryParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            "tap" => InputMode.Tap,
            "hold" => InputMode.Hold,
            "double_tap" or "doubletap" => InputMode.DoubleTap,
            "press" => InputMode.Press,
            "release" => InputMode.Release,
            _ => null,
        };
    }

    private static InputDevice ParseDevice(string? value) => value?.ToLowerInvariant() switch
    {
        "mouse" => InputDevice.Mouse,
        "gamepad" => InputDevice.Gamepad,
        _ => InputDevice.Keyboard,
    };

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int number)
            ? number
            : null;

    private static bool? GetBool(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out JsonElement value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static List<string> GetStringArray(JsonElement element, string property)
    {
        List<string> values = new();

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out JsonElement array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string? text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text);
                    }
                }
            }
        }

        return values;
    }
}
