namespace Optimus.Core.Abstractions;

/// <summary>Ce que le moteur d'écoute a entendu.</summary>
/// <param name="Text">Phrase reconnue, telle qu'elle figure dans la grammaire.</param>
/// <param name="Confidence">Confiance du moteur, de 0 à 1.</param>
/// <param name="CommandId">Commande désignée, si la phrase appartient à la grammaire.</param>
/// <param name="Accepted">La confiance atteint-elle le seuil configuré.</param>
/// <param name="RecognizedAt">Instant de la reconnaissance.</param>
public sealed record VoiceRecognition(
    string Text,
    double Confidence,
    string? CommandId,
    bool Accepted,
    DateTimeOffset RecognizedAt)
{
    public override string ToString() =>
        $"« {Text} » conf {Confidence:F2}{(Accepted ? string.Empty : " (sous le seuil)")}";
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
