using Optimus.Core.Domain.Personality;

namespace Optimus.Core.Domain.Copilots;

/// <summary>Voix du copilote.</summary>
/// <param name="Provider">Moteur de synthèse : <c>windows-onecore</c>, <c>piper</c>…</param>
/// <param name="VoiceId">Voix précise. Null = voix par défaut du moteur pour la langue.</param>
/// <param name="Rate">Débit. Le <see cref="PersonalityTraits.SpeechRate"/> le module aussi.</param>
/// <param name="Volume">Volume, de 0 à 1.</param>
public sealed record VoiceConfig(
    string Provider = "windows-onecore",
    string? VoiceId = null,
    double Rate = 1.0,
    double Volume = 0.9);

/// <summary>
/// Un copilote : une identité, une voix, un caractère, des répliques.
///
/// Le concept central du produit (§7 du cahier des charges) : Optimus, Synthia ou Virgil ne
/// sont pas trois programmes mais <b>trois jeux de données</b>. Créer « Optimus Combat » ne
/// demande pas une ligne de C#.
/// </summary>
public sealed record Copilot(
    string Id,
    string Name,
    string Language,
    string WakeWord,
    VoiceConfig Voice,
    Domain.Personality.Personality Personality,
    ResponseSet Responses,
    string? Description = null,
    string? AccentColor = null)
{
    /// <summary>Débit effectif : celui de la voix, modulé par le calme du personnage.</summary>
    public double EffectiveRate => Math.Round(Voice.Rate * Personality.Traits.SpeechRate, 3);

    /// <summary>Copilote minimal, pour les tests et le tout premier démarrage.</summary>
    public static Copilot Fallback { get; } = new(
        "optimus",
        "Optimus",
        "fr-FR",
        "Optimus",
        new VoiceConfig(),
        Domain.Personality.Personality.Default,
        ResponseSet.Empty);
}
