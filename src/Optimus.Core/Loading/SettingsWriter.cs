using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Domain.Profiles;

namespace Optimus.Core.Loading;

/// <summary>
/// Écriture des réglages modifiables depuis l'interface.
///
/// <b>Patche</b> les fichiers plutôt que de les régénérer, et c'est la seule façon acceptable de
/// procéder : ces fichiers contiennent bien plus que ce que l'interface expose — les tableaux
/// <c>notes</c> qui expliquent les choix, le lexique, les règles de comportement, les clés qu'une
/// version future ajoutera. Sérialiser le modèle par-dessus effacerait tout cela en silence, et
/// personne ne s'en apercevrait avant d'avoir perdu son travail.
///
/// On relit donc l'arbre JSON, on remplace les valeurs visées, on réécrit le reste tel quel.
/// </summary>
public static class SettingsWriter
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,

        // Les noeuds construits a la main - JsonArray.Add, l'indexeur - sont enveloppes dans un
        // JsonValue generique, que la serialisation refuse d'ecrire sans resolveur de types.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),

        // Sans cela, les accents et les apostrophes ressortent en séquences é, illisibles
        // dans un fichier que l'utilisateur a le droit d'ouvrir et de modifier à la main.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Écrit les réglages d'écoute dans le profil utilisateur.</summary>
    public static void SaveVoiceInput(string profilePath, VoiceInputSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(settings);

        Patch(profilePath, root =>
        {
            JsonObject voice = Section(root, "voice_input");

            voice["mode"] = settings.Mode == ListeningMode.PushToTalk ? "push_to_talk" : "always_on";
            voice["push_to_talk_key"] = settings.PushToTalkKey;
            voice["require_wake_word_in_push_to_talk"] = settings.RequireWakeWordInPushToTalk;
            voice["confidence_threshold"] = Math.Round(settings.ConfidenceThreshold, 3);
            voice["noise_floor"] = Math.Round(settings.NoiseFloor, 3);
        });
    }

    /// <summary>
    /// Écrit le profil de touches actif.
    ///
    /// Dans le profil utilisateur et non dans le fichier du profil de touches : c'est un
    /// réglage de la machine, pas du jeu d'assignations. Deux postes qui partagent les mêmes
    /// profils peuvent ainsi en avoir chacun un d'actif différent.
    /// </summary>
    public static void SaveActiveBindingProfile(string profilePath, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Patch(profilePath, root => root["active_binding_profile"] = name);
    }

    /// <summary>Écrit les réglages de l'étage conversationnel.</summary>
    public static void SaveAi(string profilePath, Ai.AiSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profilePath);
        ArgumentNullException.ThrowIfNull(settings);

        Patch(profilePath, root =>
        {
            JsonObject ai = Section(root, "ai");

            ai["enabled"] = settings.Enabled;
            ai["provider"] = settings.Provider;
            ai["endpoint"] = settings.Endpoint;
            ai["model"] = settings.Model;
            ai["call_budget"] = settings.CallBudget;
        });
    }

    /// <summary>Écrit la voix et le mot d'éveil dans la fiche du copilote.</summary>
    public static void SaveCopilotVoice(string copilotPath, VoiceConfig voice, string wakeWord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copilotPath);
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentException.ThrowIfNullOrWhiteSpace(wakeWord);

        Patch(copilotPath, root =>
        {
            root["wake_word"] = wakeWord;

            JsonObject node = Section(root, "voice");
            node["provider"] = voice.Provider;
            node["voice_id"] = voice.VoiceId;
            node["rate"] = Math.Round(voice.Rate, 3);
            node["volume"] = Math.Round(voice.Volume, 3);
        });
    }

    /// <summary>Écrit les curseurs de caractère, sans toucher au lexique ni aux règles.</summary>
    public static void SaveTraits(string personalityPath, PersonalityTraits traits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalityPath);
        ArgumentNullException.ThrowIfNull(traits);

        Patch(personalityPath, root =>
        {
            JsonObject node = Section(root, "traits");

            node["humor"] = traits.Humor;
            node["sarcasm"] = traits.Sarcasm;
            node["formality"] = traits.Formality;
            node["verbosity"] = traits.Verbosity;
            node["aggression"] = traits.Aggression;
            node["calmness"] = traits.Calmness;
            node["warmth"] = traits.Warmth;
            node["confidence"] = traits.Confidence;
        });
    }

    /// <summary>
    /// Applique une modification puis réécrit le fichier.
    ///
    /// L'écriture passe par un fichier temporaire et un remplacement : une coupure de courant au
    /// mauvais moment laisserait sinon un JSON tronqué, et Optimus refuserait de démarrer avec
    /// pour seul indice une erreur d'analyse.
    /// </summary>
    private static void Patch(string path, Action<JsonObject> change)
    {
        if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
        {
            throw new InvalidOperationException($"« {Path.GetFileName(path)} » n'est pas un objet JSON.");
        }

        change(root);

        // Le saut de ligne final n'est pas une coquetterie : sans lui, git signale le fichier
        // comme modifie a chaque ecriture, et le diff d'un reglage change se lit deux fois moins
        // bien qu'il ne le devrait.
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(Format) + Environment.NewLine);
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonObject Section(JsonObject root, string name)
    {
        if (root[name] is JsonObject existing)
        {
            return existing;
        }

        JsonObject created = new();
        root[name] = created;
        return created;
    }
}
