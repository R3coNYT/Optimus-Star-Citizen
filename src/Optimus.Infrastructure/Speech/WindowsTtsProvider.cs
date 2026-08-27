using System.Runtime.Versioning;
using Optimus.Core.Abstractions;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Synthèse vocale par les voix OneCore de Windows.
///
/// L'API <c>Windows.Media.SpeechSynthesis</c> est retenue plutôt que <c>System.Speech</c> pour
/// une raison mesurée au spike S0-5 : SAPI n'expose qu'une seule voix française, féminine
/// (Hortense), là où OneCore en expose trois — dont <b>Paul</b>, la seule voix masculine
/// française installée. Pour un copilote militaire, ce n'est pas un détail de confort.
///
/// Latence mesurée : 7 à 15 ms par réplique une fois le moteur chaud, soit un facteur temps
/// réel de 0,003. Mais jusqu'à 429 ms pour la toute première — d'où <see cref="WarmUpAsync"/>,
/// à appeler au démarrage (décision D23).
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class WindowsTtsProvider : ITextToSpeechProvider
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly MediaPlayer _player = new();
    private readonly SemaphoreSlim _speaking = new(1, 1);
    private bool _disposed;

    public string Id => "windows-onecore";

    public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        List<VoiceInfo> voices = new();

        foreach (VoiceInformation voice in SpeechSynthesizer.AllVoices)
        {
            voices.Add(new VoiceInfo(
                voice.Id,
                voice.DisplayName,
                voice.Language,
                voice.Gender == VoiceGender.Male));
        }

        return Task.FromResult<IReadOnlyList<VoiceInfo>>(voices);
    }

    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return;
        }

        // Une seule réplique à la fois : deux voix qui se chevauchent sont inintelligibles.
        // Le futur ordonnanceur de parole (multi-copilotes, V2) prendra le relais ici.
        await _speaking.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SelectVoice(request.VoiceId);

            _synthesizer.Options.SpeakingRate = Math.Clamp(request.Rate, 0.5, 6.0);
            _synthesizer.Options.AudioVolume = Math.Clamp(request.Volume, 0.0, 1.0);

            using SpeechSynthesisStream stream = await _synthesizer
                .SynthesizeTextToStreamAsync(request.Text)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnEnded(MediaPlayer sender, object args) => completion.TrySetResult();
            void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
                completion.TrySetException(new InvalidOperationException(
                    $"Lecture audio impossible : {args.ErrorMessage}"));

            _player.MediaEnded += OnEnded;
            _player.MediaFailed += OnFailed;

            try
            {
                _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                _player.Play();

                using CancellationTokenRegistration registration =
                    cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

                await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                _player.MediaEnded -= OnEnded;
                _player.MediaFailed -= OnFailed;
            }
        }
        finally
        {
            _speaking.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Interruption immédiate : c'est ce qui permettra au pilote de couper la parole du
        // copilote quand il reprend le micro (barge-in).
        _player.Pause();
        _player.Source = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Initialise le moteur en synthétisant un texte que personne n'entendra : le volume est
    /// mis à zéro et le flux n'est jamais lu. Seul compte le coût d'initialisation, payé ici
    /// plutôt qu'au moment de la première vraie réplique.
    /// </summary>
    public async Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        SelectVoice(voiceId);

        using SpeechSynthesisStream stream = await _synthesizer
            .SynthesizeTextToStreamAsync("Initialisation.")
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        _ = stream.Size;
    }

    /// <summary>
    /// Sélectionne une voix par identifiant ou par nom affiché.
    ///
    /// Accepter le nom affiché est délibéré : c'est ce que voit l'utilisateur dans l'interface
    /// et ce qu'il écrira dans son fichier de copilote. Lui imposer un identifiant technique
    /// serait une fausse rigueur.
    /// </summary>
    private void SelectVoice(string? voiceId)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            return;
        }

        foreach (VoiceInformation voice in SpeechSynthesizer.AllVoices)
        {
            if (string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voice.DisplayName, voiceId, StringComparison.OrdinalIgnoreCase))
            {
                _synthesizer.Voice = voice;
                return;
            }
        }

        // Voix introuvable : on garde celle par défaut plutôt que d'échouer. Une voix
        // inattendue vaut mieux qu'un copilote muet, et l'anomalie se voit à l'oreille.
        //
        // Elle se lit aussi, desormais : le cas le plus frequent est un identifiant Piper arrive
        // ici apres un repli, et un pilote qui entend soudain une autre voix merite mieux qu'une
        // devinette.
        Optimus.Core.Diagnostics.DiagnosticLog.Warn(
            $"voix Windows « {voiceId} » introuvable",
            "La voix par défaut du système prend le relais.");
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _player.Dispose();
        _synthesizer.Dispose();
        _speaking.Dispose();

        return ValueTask.CompletedTask;
    }
}
