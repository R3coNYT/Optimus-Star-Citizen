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
            SelectVoice(request.VoiceId, request.Language);

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

        SelectVoice(voiceId, language: null);

        using SpeechSynthesisStream stream = await _synthesizer
            .SynthesizeTextToStreamAsync("Initialisation.")
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        _ = stream.Size;
    }

    /// <summary>
    /// Sélectionne une voix par identifiant, par nom affiché, ou à défaut par langue.
    ///
    /// Accepter le nom affiché est délibéré : c'est ce que voit l'utilisateur dans l'interface
    /// et ce qu'il écrira dans son fichier de copilote. Lui imposer un identifiant technique
    /// serait une fausse rigueur.
    ///
    /// Le repli par langue répare un défaut mesuré le 2026-08-29, sur une machine dont Windows
    /// s'affiche en français. Un copilote sans voix imposée (<c>"voice_id": null</c>, ce que
    /// livre Optimus) tombait sur la voix par défaut du système, donc Hortense : le copilote
    /// passé en anglais prononçait un texte anglais avec une voix française. Le contrat
    /// l'annonçait pourtant depuis toujours — <c>VoiceConfig.VoiceId</c> dit « null = voix par
    /// défaut du moteur <b>pour la langue</b> ». Il n'était simplement pas tenu.
    /// </summary>
    private void SelectVoice(string? voiceId, string? language)
    {
        if (!string.IsNullOrWhiteSpace(voiceId))
        {
            foreach (VoiceInformation voice in SpeechSynthesizer.AllVoices)
            {
                if (string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(voice.DisplayName, voiceId, StringComparison.OrdinalIgnoreCase))
                {
                    _synthesizer.Voice = voice;
                    return;
                }
            }

            // Voix introuvable : on ne s'arrête pas là. Une voix inattendue vaut mieux qu'un
            // copilote muet, et l'anomalie se voit à l'oreille.
            //
            // Elle se lit aussi, desormais : le cas le plus frequent est un identifiant Piper
            // arrive ici apres un repli, et un pilote qui entend soudain une autre voix merite
            // mieux qu'une devinette.
            Optimus.Core.Diagnostics.DiagnosticLog.Warn(
                $"Windows voice “{voiceId}” not found",
                "Optimus falls back to a voice in the copilot's language.");
        }

        VoiceInformation? matching = MatchLanguage(language);

        if (matching is null && !string.IsNullOrWhiteSpace(language))
        {
            // Le cas se produit vraiment : Windows installé en français n'a que des voix
            // françaises, et un copilote passé en anglais parlera anglais avec l'accent
            // français tant que le module vocal n'est pas posé. Autant le dire, parce que le
            // pilote qui entend ça croira à un bogue d'Optimus.
            //
            // Attention au piège : ces voix-là sont celles de OneCore, pas celles de SAPI.
            // Windows livre Zira (anglaise) à SAPI sur toutes les machines, et elle n'apparait
            // pas ici. Lire la mauvaise liste ferait conclure que tout va bien.
            Optimus.Core.Diagnostics.DiagnosticLog.Warn(
                $"no Windows voice installed for “{language}”",
                "The copilot keeps the system default voice. "
                + "Add the “text-to-speech” feature for that language in "
                + "Settings ▸ Time & language.");
        }

        // Remettre la voix par défaut quand rien ne correspond n'est pas une politesse : le
        // synthétiseur garde la voix posée à l'appel précédent, et sans cette ligne un essai de
        // voix dans les réglages teindrait toutes les répliques suivantes.
        _synthesizer.Voice = matching ?? SpeechSynthesizer.DefaultVoice;
    }

    /// <summary>
    /// Première voix installée dans cette langue, étiquette complète d'abord (<c>en-US</c>),
    /// puis code de langue seul (<c>en</c>) — une voix britannique dit l'anglais bien mieux
    /// qu'une voix française.
    /// </summary>
    internal static VoiceInformation? MatchLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        IReadOnlyList<VoiceInformation> voices = SpeechSynthesizer.AllVoices;

        foreach (VoiceInformation voice in voices)
        {
            if (string.Equals(voice.Language, language, StringComparison.OrdinalIgnoreCase))
            {
                return voice;
            }
        }

        string prefix = language.Split('-')[0];

        foreach (VoiceInformation voice in voices)
        {
            if (voice.Language.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return voice;
            }
        }

        return null;
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
