using System.Text.Json;
using Optimus.Core.Domain.Profiles;

namespace Optimus.Core.Loading;

/// <summary>Réglages d'un utilisateur.</summary>
/// <param name="Id">Identifiant du profil.</param>
/// <param name="DisplayName">Nom affiché.</param>
/// <param name="PreferredCopilot">Copilote actif.</param>
/// <param name="VoiceInput">Mode d'écoute et réglages de capture.</param>
/// <param name="KillSwitchKey">Raccourci d'arrêt d'urgence.</param>
/// <param name="SimulationMode">Mode simulation actif au démarrage.</param>
/// <param name="RequireGameForeground">Exiger le focus du jeu avant d'envoyer une entrée.</param>
/// <param name="ConfirmDangerous">Confirmer les commandes marquées dangereuses.</param>
public sealed record UserProfile(
    string Id,
    string DisplayName,
    string PreferredCopilot,
    VoiceInputSettings VoiceInput,
    string KillSwitchKey = "CTRL+ALT+PAUSE",
    bool SimulationMode = false,
    bool RequireGameForeground = true,
    bool ConfirmDangerous = true)
{
    public static UserProfile Default { get; } =
        new("default", "Pilote", "optimus", VoiceInputSettings.Default);
}

/// <summary>Lecture d'un profil utilisateur.</summary>
public static class ProfileLoader
{
    public static LoadResult<UserProfile> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        List<LoadIssue> issues = new();

        if (!File.Exists(path))
        {
            issues.Add(new LoadIssue(path, null, "Profil introuvable, valeurs par défaut appliquées."));
            return new LoadResult<UserProfile>(UserProfile.Default, issues);
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        VoiceInputSettings voiceInput = VoiceInputSettings.Default;

        if (root.TryGetProperty("voice_input", out JsonElement voice) && voice.ValueKind == JsonValueKind.Object)
        {
            string? rawMode = GetString(voice, "mode");
            ListeningMode mode = rawMode?.ToLowerInvariant() switch
            {
                "always_on" or "alwayson" or "active" => ListeningMode.AlwaysOn,
                "push_to_talk" or "pushtotalk" or "ptt" => ListeningMode.PushToTalk,
                null => ListeningMode.AlwaysOn,
                _ => Unknown(rawMode, issues, path),
            };

            voiceInput = new VoiceInputSettings(
                mode,
                GetString(voice, "push_to_talk_key") ?? "INSERT",
                GetBool(voice, "require_wake_word_in_push_to_talk") ?? false,
                GetDouble(voice, "confidence_threshold") ?? 0.65,
                GetDouble(voice, "noise_floor") ?? 0.35,
                GetString(voice, "input_device_id"));
        }

        UserProfile profile = new(
            GetString(root, "id") ?? Path.GetFileNameWithoutExtension(path),
            GetString(root, "display_name") ?? "Pilote",
            GetString(root, "preferred_copilot") ?? "optimus",
            voiceInput,
            GetString(root, "hotkeys", "kill_switch") ?? "CTRL+ALT+PAUSE",
            GetBool(root, "safety", "simulation_mode") ?? false,
            GetBool(root, "safety", "require_game_foreground") ?? true,
            GetBool(root, "safety", "confirm_dangerous") ?? true);

        return new LoadResult<UserProfile>(profile, issues);
    }

    /// <summary>
    /// Un mode inconnu ne bloque pas le démarrage : on retombe sur l'écoute permanente, qui est
    /// le défaut, et l'anomalie remonte dans le rapport de chargement.
    /// </summary>
    private static ListeningMode Unknown(string? raw, List<LoadIssue> issues, string path)
    {
        issues.Add(new LoadIssue(path, "voice_input.mode", $"Mode « {raw} » inconnu, écoute permanente appliquée."));
        return ListeningMode.AlwaysOn;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetString(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out JsonElement container) ? GetString(container, property) : null;

    private static double? GetDouble(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out double number)
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

    private static bool? GetBool(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out JsonElement container) ? GetBool(container, property) : null;
}
