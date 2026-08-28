using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Abstractions;

/// <summary>
/// Ce qu'il convient de faire d'une reconnaissance.
///
/// Trois issues plutôt que deux, parce qu'un moteur à grammaire ne sait pas dire « je ne
/// connais pas cette phrase » : il rend toujours sa meilleure alternative avec une confiance.
/// Distinguer le bruit d'une interpellation mal comprise est donc à notre charge, et c'est
/// cette distinction qui évite à la fois de bavarder sur du bruit et d'exécuter une commande
/// que personne n'a demandée.
/// </summary>
public enum RecognitionOutcome
{
    /// <summary>Sous le plancher de bruit : on ne réagit pas.</summary>
    Noise,

    /// <summary>
    /// Le copilote a été interpellé mais la commande n'est pas sûre. Il doit le dire — et,
    /// à terme, transmettre l'énoncé à l'étage conversationnel (décision D28).
    /// </summary>
    Unclear,

    /// <summary>Commande reconnue avec assez de confiance pour être exécutée.</summary>
    Accepted,
}

/// <summary>Ce que le moteur d'écoute a entendu.</summary>
/// <param name="Text">Phrase reconnue, telle qu'elle figure dans la grammaire.</param>
/// <param name="Confidence">Confiance du moteur, de 0 à 1.</param>
/// <param name="CommandId">Commande désignée, si la phrase appartient à la grammaire.</param>
/// <param name="Outcome">Ce qu'il convient d'en faire.</param>
/// <param name="RecognizedAt">Instant de la reconnaissance.</param>
/// <param name="Polarity">
/// Sens demandé, quand la phrase le dit. « Éteins les lumières » et « allume les lumières »
/// désignent la même commande : sans cette précision, la seconde ferait office de première.
/// </param>
public sealed record VoiceRecognition(
    string Text,
    double Confidence,
    string? CommandId,
    RecognitionOutcome Outcome,
    DateTimeOffset RecognizedAt,
    CommandPolarity Polarity = CommandPolarity.Neutral,
    string? AudioPath = null)
{
    /// <summary>
    /// Fichier WAV de ce qui a été entendu, quand l'étage de parole libre le demande.
    ///
    /// Un chemin et non des octets : c'est ce qu'attend whisper.cpp, et cela évite de porter
    /// plusieurs secondes de son dans un enregistrement qui traverse trois couches. Le fichier
    /// est temporaire et appartient à celui qui le consomme — à lui de l'effacer.
    /// </summary>
    public bool HasAudio => AudioPath is not null && File.Exists(AudioPath);

    public bool Accepted => Outcome == RecognitionOutcome.Accepted;

    public override string ToString() => $"« {Text} » conf {Confidence:F2}" + Outcome switch
    {
        RecognitionOutcome.Noise => " (bruit)",
        RecognitionOutcome.Unclear => " (interpellé, mais pas compris)",
        _ => string.Empty,
    };
}

/// <summary>
/// Écoute du microphone et reconnaissance de commandes.
///
/// Volontairement distinct d'un <c>ISpeechToTextProvider</c> : celui-ci transcrit librement,
/// celui-là ne peut restituer qu'une phrase autorisée. Les deux coexisteront — le premier pour
/// la conversation, le second pour les commandes (décision D28) — mais ils ne rendent pas le
/// même service et ne méritent pas la même interface.
/// </summary>
public interface IVoiceCommandListener : IAsyncDisposable
{
    /// <summary>Identifiant du moteur, pour les journaux et l'interface.</summary>
    string Id { get; }

    /// <summary>Vrai lorsque le micro est écouté.</summary>
    bool IsListening { get; }

    /// <summary>
    /// Une phrase a été reconnue. Émis même sous le seuil de confiance : c'est en observant
    /// les rejets qu'on calibre ce seuil, et l'utilisateur a le droit de savoir qu'il a été
    /// entendu sans être compris.
    /// </summary>
    event EventHandler<VoiceRecognition>? Recognized;

    /// <summary>Démarre l'écoute.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Arrête l'écoute sans libérer le moteur.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Active ou suspend la prise en compte de la parole.
    ///
    /// Utilisé par le push-to-talk : la grammaire est désactivée hors appui, de sorte que le
    /// moteur n'a plus rien à reconnaître. À la différence d'un arrêt complet, cela évite de
    /// rouvrir le périphérique — opération mesurée à 419 ms au spike S0-3, soit de quoi
    /// tronquer le début de chaque phrase (décision D24).
    /// </summary>
    void SetActive(bool active);
}
