namespace Optimus.Core.Speech;

/// <summary>
/// Quand l'étage de parole libre intervient.
///
/// Le choix appartient au pilote parce que les deux compromis se défendent, et qu'aucune mesure
/// prise sur une autre voix que la sienne ne peut trancher à sa place.
/// </summary>
public enum WhisperMode
{
    /// <summary>
    /// Éteint. Optimus s'en tient à sa grammaire fermée.
    ///
    /// Le défaut, et la seule position où la promesse d'origine tient mot pour mot : ce qui n'est
    /// pas une commande connue n'est jamais transcrit, nulle part.
    /// </summary>
    Off,

    /// <summary>
    /// Quand le moteur rapide n'est pas sûr : rejets <b>et</b> reconnaissances douteuses.
    ///
    /// Le moteur rapide garde la main sur ce qu'il reconnaît franchement — instantané, sûr,
    /// éprouvé — et Whisper reçoit tout le reste. Coût sur les commandes bien reconnues :
    /// <b>aucun</b>.
    ///
    /// <b>Les douteuses comptent autant que les rejets</b>, et l'essai en direct du 2026-08-28
    /// l'a montré : « qu'est-ce que tu penses de ce vaisseau ? » n'a pas été rejeté, il a été
    /// rattaché à « qu'est-ce que tu as dit » avec 0,51 de confiance. S'en tenir aux rejets
    /// aurait laissé passer le cas le plus fréquent — et c'est aussi la seconde chance des vraies
    /// commandes mal entendues, comme « prépare le décollage » à 0,41 (D39).
    /// </summary>
    Rejected,

    /// <summary>
    /// Sur tout, commandes comprises.
    ///
    /// Le rapprochement flou travaille alors sur le texte réellement prononcé plutôt que sur une
    /// liste d'alternatives — mais chaque commande paie la transcription. Mesuré le 2026-08-28 :
    /// <b>environ 900 ms par énoncé</b>, et 9,8 % de mots erronés là où la grammaire fermée rend
    /// 0,825 de confiance. À essayer sur sa propre voix avant de s'y tenir.
    /// </summary>
    Always,
}

/// <summary>Réglages de l'étage de parole libre.</summary>
/// <param name="Mode">Quand Whisper intervient.</param>
/// <param name="Model">
/// Modèle employé. <c>base</c> par défaut, tranché par la mesure et non par l'intuition (D26) :
/// <c>small</c> n'est pas plus précis sur ce vocabulaire pour 3,4× le temps, et <c>tiny</c>
/// s'effondre.
/// </param>
/// <param name="Threads">
/// Fils d'exécution. Zéro signifie « autant que de processeurs logiques ». Le SMT aide
/// réellement, contrairement à l'intuition — mesuré en S0-2, et confirmé en S0-7 : 4 fils coûtent
/// 1 213 ms là où 8 en coûtent 905.
/// </param>
/// <param name="TrimContext">
/// Réduire la fenêtre d'encodage à 768 au lieu de 1 500. Fait passer la transcription de 905 à
/// 536 ms, au prix d'un taux d'erreur qui monte de 9,8 % à 14,8 % (S0-2). Un échange que le
/// pilote doit pouvoir faire, pas un défaut qu'on lui impose.
/// </param>
public sealed record WhisperSettings(
    WhisperMode Mode = WhisperMode.Off,
    string Model = "base",
    int Threads = 0,
    bool TrimContext = false)
{
    public static WhisperSettings Disabled { get; } = new();

    /// <summary>Vrai si l'étage doit être monté.</summary>
    public bool Enabled => Mode != WhisperMode.Off;

    /// <summary>
    /// Fenêtre d'encodage demandée à whisper.cpp, ou zéro pour la fenêtre complète.
    ///
    /// 768 et non 512 : à 512 le taux d'erreur passe à 31 %, ce qui ne transcrit plus rien
    /// d'exploitable. Le gain de 90 ms supplémentaires ne vaut pas ce prix.
    /// </summary>
    public int AudioContext => TrimContext ? 768 : 0;

    /// <summary>Fils réellement employés.</summary>
    public int EffectiveThreads => Threads > 0 ? Threads : Environment.ProcessorCount;
}
