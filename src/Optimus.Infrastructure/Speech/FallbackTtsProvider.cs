using Optimus.Core.Abstractions;
using Optimus.Core.Diagnostics;

namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Un moteur de synthèse, doublé d'un second au cas où le premier tombe.
///
/// La raison d'être tient en une phrase : <b>rien ne doit pouvoir rendre le copilote muet.</b>
/// Piper est un processus externe, avec tout ce que cela suppose — un antivirus qui le tue, un
/// modèle corrompu, un disque plein. Les voix Windows, elles, sont toujours là. Perdre le timbre
/// est un désagrément ; perdre la parole en vol en est un autre.
///
/// <b>La rétrogradation est le point important.</b> Réessayer indéfiniment ferait payer le délai
/// d'attente de Piper — vingt secondes — à <i>chaque</i> réplique, ce qui serait bien pire qu'un
/// simple changement de voix. Après deux échecs consécutifs, le moteur principal est abandonné
/// pour la session : le pilote entend une autre voix, ce qui est un signal en soi, et le journal
/// dit pourquoi.
/// </summary>
public sealed class FallbackTtsProvider : ITextToSpeechProvider
{
    /// <summary>Échecs consécutifs tolérés avant d'abandonner le moteur principal.</summary>
    private const int Tolerance = 2;

    private readonly ITextToSpeechProvider _primary;
    private readonly ITextToSpeechProvider _fallback;

    private int _failures;
    private bool _demoted;

    public FallbackTtsProvider(ITextToSpeechProvider primary, ITextToSpeechProvider fallback)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(fallback);

        _primary = primary;
        _fallback = fallback;
    }

    /// <summary>Le moteur réellement à l'œuvre — celui-ci change si le principal est abandonné.</summary>
    public string Id => Active.Id;

    /// <summary>Vrai si le moteur principal a été abandonné pour la session.</summary>
    public bool IsDemoted => _demoted;

    private ITextToSpeechProvider Active => _demoted ? _fallback : _primary;

    /// <summary>
    /// Les voix des deux moteurs, celles du principal d'abord.
    ///
    /// Les deux, et pas seulement celles du moteur actif : le pilote doit pouvoir choisir une
    /// voix Windows depuis les réglages sans avoir à désactiver Piper d'abord.
    /// </summary>
    public async Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        List<VoiceInfo> voices = new(await _primary.GetVoicesAsync(cancellationToken).ConfigureAwait(false));

        voices.AddRange(await _fallback.GetVoicesAsync(cancellationToken).ConfigureAwait(false));

        return voices;
    }

    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_demoted)
        {
            await _fallback.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _primary.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
            _failures = 0;
            return;
        }
        catch (OperationCanceledException)
        {
            // Une interruption demandee n'est pas une panne : le pilote a repris la parole.
            throw;
        }
        catch (Exception exception)
        {
            _failures++;

            DiagnosticLog.Warn(
                $"{_primary.Id} failed ({_failures}/{Tolerance})",
                exception.Message);

            if (_failures >= Tolerance)
            {
                _demoted = true;

                DiagnosticLog.Warn(
                    $"switching for good to “{_fallback.Id}” for this session",
                    $"“{_primary.Id}” failed {_failures} times in a row. Retrying would charge "
                    + "its timeout to every single reply.");
            }
        }

        // Le repli, hors du bloc « catch » : une exception ici doit remonter telle quelle,
        // sans etre presentee comme la consequence de la premiere.
        await _fallback.SpeakAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            _primary.StopAsync(cancellationToken),
            _fallback.StopAsync(cancellationToken));

    /// <summary>
    /// Préchauffe les deux : le repli doit être prêt <b>avant</b> qu'on en ait besoin, sinon la
    /// première réplique après une panne paierait ses 429 ms d'initialisation (D23) en plus de
    /// l'échec qui l'a provoquée.
    /// </summary>
    public async Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await _primary.WarmUpAsync(voiceId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Un prechauffage rate ne condamne pas le moteur : la premiere replique retentera.
            DiagnosticLog.Warn($"could not warm up “{_primary.Id}”", exception.Message);
        }

        await _fallback.WarmUpAsync(null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _primary.DisposeAsync().ConfigureAwait(false);
        await _fallback.DisposeAsync().ConfigureAwait(false);
    }
}
