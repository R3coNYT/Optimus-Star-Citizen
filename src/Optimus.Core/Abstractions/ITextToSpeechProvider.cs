namespace Optimus.Core.Abstractions;

/// <summary>Une voix disponible sur la machine.</summary>
/// <param name="Id">Identifiant technique, tel qu'attendu par le moteur.</param>
/// <param name="DisplayName">Nom affichable.</param>
/// <param name="Language">Étiquette de langue, par exemple <c>fr-FR</c>.</param>
/// <param name="IsMale">Genre déclaré, quand le moteur le fournit.</param>
public sealed record VoiceInfo(string Id, string DisplayName, string Language, bool? IsMale = null)
{
    public override string ToString() =>
        $"{DisplayName} ({Language}{(IsMale is null ? string.Empty : IsMale.Value ? ", masculine" : ", féminine")})";
}

/// <summary>Demande de synthèse.</summary>
/// <param name="Text">Texte à prononcer.</param>
/// <param name="VoiceId">Voix souhaitée. Null = voix par défaut du moteur.</param>
/// <param name="Rate">Débit, 1.0 étant le débit naturel de la voix.</param>
/// <param name="Volume">Volume, de 0 à 1.</param>
public sealed record SpeechRequest(string Text, string? VoiceId = null, double Rate = 1.0, double Volume = 1.0);

/// <summary>
/// Synthèse vocale.
///
/// Mesuré au spike S0-5 : les voix Windows synthétisent une réplique en 7 à 15 ms, soit un
/// facteur temps réel de 0,003. La parole n'est donc pas un problème de latence — à une
/// condition, que <see cref="WarmUpAsync"/> soit appelée au démarrage : la toute première
/// synthèse coûte jusqu'à 429 ms, et ce serait précisément la première phrase entendue par
/// l'utilisateur (décision D23).
/// </summary>
public interface ITextToSpeechProvider : IAsyncDisposable
{
    /// <summary>Identifiant du moteur : <c>windows-onecore</c>, <c>piper</c>, <c>elevenlabs</c>…</summary>
    string Id { get; }

    /// <summary>Voix disponibles, toutes langues confondues.</summary>
    Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default);

    /// <summary>Prononce le texte et rend la main quand la lecture est terminée.</summary>
    Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default);

    /// <summary>Interrompt la parole en cours, s'il y en a une.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialise le moteur à l'avance, pour que la première réplique ne soit pas la plus lente.
    /// </summary>
    Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default);
}

/// <summary>Moteur muet : le pipeline complet reste exécutable là où il n'y a pas d'audio.</summary>
public sealed class NullTextToSpeechProvider : ITextToSpeechProvider
{
    private readonly List<string> _spoken = new();

    public string Id => "null";

    /// <summary>Ce qui aurait été prononcé, dans l'ordre.</summary>
    public IReadOnlyList<string> Spoken => _spoken;

    public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VoiceInfo>>(Array.Empty<VoiceInfo>());

    public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _spoken.Add(request.Text);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
