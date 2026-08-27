using System.Text.Json;
using Optimus.Core.Abstractions;

namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Une installation de Piper trouvée sur la machine.
///
/// Piper n'est pas livré avec Optimus, et ce n'est pas un oubli : le binaire pèse 22 Mo et
/// chaque voix 63 de plus, pour une fonction dont le pilote peut très bien se passer — les voix
/// Windows suffisent en latence (spike S0-5), l'enjeu est le timbre. Le distribuer imposerait
/// 150 Mo à tout le monde pour le confort de quelques-uns.
///
/// L'installation vit donc dans <c>%APPDATA%\Optimus\piper</c>, comme les touches (D35), les
/// macros (D43) et les formulations apprises (D46) : hors de <c>data/</c>, que le script de
/// publication remplace à chaque mise à jour.
/// </summary>
/// <param name="Executable">Chemin complet de <c>piper.exe</c>.</param>
/// <param name="VoicesDirectory">Dossier des modèles <c>.onnx</c>.</param>
public sealed record PiperInstallation(string Executable, string VoicesDirectory)
{
    /// <summary>Dossier attendu, sous les données du pilote.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Optimus",
        "piper");

    /// <summary>
    /// Cherche une installation utilisable, ou rend <c>null</c>.
    ///
    /// Exige le binaire <b>et</b> au moins une voix : un Piper sans modèle est une installation
    /// à moitié faite, et laisser Optimus la choisir reviendrait à le rendre muet le temps que
    /// le pilote comprenne pourquoi.
    /// </summary>
    public static PiperInstallation? Locate(string? root = null)
    {
        string directory = root ?? DefaultRoot;
        string executable = Path.Combine(directory, "piper.exe");

        if (!File.Exists(executable))
        {
            return null;
        }

        string voices = Path.Combine(directory, "voices");

        if (!Directory.Exists(voices) || Directory.GetFiles(voices, "*.onnx").Length == 0)
        {
            return null;
        }

        return new PiperInstallation(executable, voices);
    }

    /// <summary>
    /// Voix installées, décrites par le fichier de configuration qui accompagne chaque modèle.
    ///
    /// Le genre n'y figure pas : Piper ne le déclare nulle part, et le déduire du nom du jeu de
    /// données serait deviner. <see cref="VoiceInfo.IsMale"/> reste donc <c>null</c> — mieux vaut
    /// ne rien dire que se tromper la moitié du temps.
    /// </summary>
    public IReadOnlyList<VoiceInfo> Voices()
    {
        List<VoiceInfo> voices = new();

        foreach (string model in Directory.EnumerateFiles(VoicesDirectory, "*.onnx").Order(StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(model);
            string language = ReadLanguage(model + ".json") ?? "?";

            voices.Add(new VoiceInfo(id, id, language));
        }

        return voices;
    }

    /// <summary>Chemin du modèle correspondant à un identifiant de voix.</summary>
    public string? ModelPath(string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return Directory.EnumerateFiles(VoicesDirectory, "*.onnx")
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
        }

        string direct = Path.Combine(VoicesDirectory, voiceId + ".onnx");

        return File.Exists(direct) ? direct : null;
    }

    private static string? ReadLanguage(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(configPath);
            using JsonDocument document = JsonDocument.Parse(stream);

            return document.RootElement.TryGetProperty("language", out JsonElement language)
                   && language.TryGetProperty("code", out JsonElement code)
                ? code.GetString()?.Replace('_', '-')
                : null;
        }
        catch (Exception)
        {
            // Un fichier de configuration illisible ne doit pas masquer la voix elle-meme :
            // elle se chargera peut-etre tres bien.
            return null;
        }
    }
}
