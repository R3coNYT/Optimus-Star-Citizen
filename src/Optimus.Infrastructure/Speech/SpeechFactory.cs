using Optimus.Core.Abstractions;
using Optimus.Core.Diagnostics;
using Optimus.Core.Domain.Copilots;

namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Choisit le moteur de parole d'après le copilote.
///
/// Le choix est une <b>donnée</b>, pas du code : <c>voice.provider</c> dans le fichier du
/// copilote, comme le prévoyait <see cref="VoiceConfig"/> depuis l'origine. Passer d'une voix
/// Windows à une voix neuronale ne demande donc pas une ligne de C#.
///
/// Partagée par l'application et le banc d'essai, et c'est délibéré : deux sélections
/// séparées finiraient par diverger, et le banc dirait alors autre chose que ce que le pilote
/// entend réellement.
/// </summary>
public static class SpeechFactory
{
    /// <summary>Identifiant de moteur demandé pour la synthèse neuronale locale.</summary>
    public const string Piper = "piper";

    /// <summary>
    /// Construit le moteur, avec son repli s'il y a lieu.
    /// </summary>
    /// <param name="copilot">Copilote dont la voix est demandée.</param>
    /// <param name="silent">
    /// Vrai pour un moteur muet : le pipeline complet reste exécutable là où personne n'écoute.
    /// </param>
    public static ITextToSpeechProvider For(Copilot copilot, bool silent = false)
    {
        ArgumentNullException.ThrowIfNull(copilot);

        if (silent)
        {
            return new NullTextToSpeechProvider();
        }

        if (!string.Equals(copilot.Voice.Provider, Piper, StringComparison.OrdinalIgnoreCase))
        {
            return new WindowsTtsProvider();
        }

        // Piper demande mais absent : les voix Windows prennent le relais. Un copilote qui
        // parle d'une autre voix vaut mieux qu'un copilote qui se tait, et le journal dit
        // pourquoi le timbre a change.
        if (PiperInstallation.Locate() is not PiperInstallation installation)
        {
            DiagnosticLog.Warn(
                "Piper requested but not found",
                $"Nothing usable in {PiperInstallation.DefaultRoot}. "
                + "Windows voices take over.");

            return new WindowsTtsProvider();
        }

        return new FallbackTtsProvider(
            new PiperTtsProvider(installation, expectedRate: copilot.EffectiveRate),
            new WindowsTtsProvider());
    }

    /// <summary>Description lisible de ce qui parlera, pour l'écran et le banc d'essai.</summary>
    public static string Describe(Copilot copilot)
    {
        ArgumentNullException.ThrowIfNull(copilot);

        if (!string.Equals(copilot.Voice.Provider, Piper, StringComparison.OrdinalIgnoreCase))
        {
            return "voix Windows";
        }

        return PiperInstallation.Locate() is PiperInstallation installation
            ? $"Piper (local) · {installation.Voices().Count} voix installées"
            : $"Piper demandé mais introuvable — repli sur les voix Windows "
              + $"(attendu dans {PiperInstallation.DefaultRoot})";
    }
}
