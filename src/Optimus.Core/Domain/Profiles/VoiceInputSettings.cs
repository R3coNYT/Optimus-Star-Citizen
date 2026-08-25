namespace Optimus.Core.Domain.Profiles;

/// <summary>Façon dont Optimus décide qu'on s'adresse à lui.</summary>
public enum ListeningMode
{
    /// <summary>
    /// Écoute permanente. Le mot d'éveil déclenche la commande.
    ///
    /// Rendu viable par le moteur à grammaire contrainte (D28) : la grammaire n'accepte alors
    /// que les phrases <b>commençant par le mot d'éveil</b>. Tout le reste — une conversation,
    /// un juron en combat — ne correspond à aucune alternative et est rejeté par construction.
    /// C'est plus sûr et bien moins coûteux qu'un détecteur de mot d'éveil séparé suivi d'une
    /// transcription libre.
    /// </summary>
    AlwaysOn,

    /// <summary>
    /// Une touche maintenue délimite la commande.
    ///
    /// Le mot d'éveil devient facultatif : la touche fait office de déclencheur. Zéro faux
    /// déclenchement possible, au prix d'un doigt occupé.
    /// </summary>
    PushToTalk,
}

/// <summary>
/// Réglages d'entrée vocale.
/// </summary>
/// <param name="Mode">Écoute permanente par défaut.</param>
/// <param name="PushToTalkKey">
/// Touche de push-to-talk. <c>INSERT</c> par défaut : vérifié libre de toute action Star Citizen
/// (décision D25). <c>F10</c> serait un mauvais choix — le jeu y a déjà deux actions.
/// </param>
/// <param name="RequireWakeWordInPushToTalk">
/// Exiger le mot d'éveil même en push-to-talk. Faux par défaut : si l'on tient déjà la touche,
/// répéter « Optimus » n'apporte rien qu'une syllabe de latence.
/// </param>
/// <param name="ConfidenceThreshold">
/// Confiance minimale pour <b>exécuter</b> une commande.
///
/// 0,65 d'après les mesures au micro du 2026-08-25 : les vraies commandes s'y situent entre
/// 0,75 et 0,93, tandis qu'une question hors catalogue (« Optimus, qui es-tu ? ») culmine à
/// 0,57. La valeur précédente de 0,40, tirée d'un banc sur fichiers, était trop basse — l'audio
/// enregistré produit des confiances plus faibles que le micro en direct.
/// </param>
/// <param name="NoiseFloor">
/// En dessous : du bruit, on se tait.
///
/// Un moteur à grammaire ne peut pas répondre « je ne connais pas cette phrase » : il rend
/// toujours sa meilleure alternative, assortie d'une confiance. Le bruit ambiant produit ainsi
/// des correspondances à 0,00–0,06. Entre ce plancher et le seuil d'exécution se trouve la zone
/// utile : on <b>a</b> été interpellé, mais on n'a pas compris — et c'est là que le copilote
/// doit le dire plutôt que de deviner.
/// </param>
/// <param name="InputDeviceId">Périphérique de capture. Null = celui du système.</param>
public sealed record VoiceInputSettings(
    ListeningMode Mode = ListeningMode.AlwaysOn,
    string PushToTalkKey = "INSERT",
    bool RequireWakeWordInPushToTalk = false,
    double ConfidenceThreshold = 0.65,
    double NoiseFloor = 0.35,
    string? InputDeviceId = null)
{
    /// <summary>
    /// Le mot d'éveil est-il obligatoire en tête d'énoncé dans ce mode ?
    ///
    /// C'est cette seule réponse qui décide de la grammaire construite : avec le mot d'éveil
    /// uniquement, ou avec les deux formes.
    /// </summary>
    public bool WakeWordRequired => Mode switch
    {
        ListeningMode.AlwaysOn => true,
        ListeningMode.PushToTalk => RequireWakeWordInPushToTalk,
        _ => true,
    };

    public static VoiceInputSettings Default { get; } = new();
}
