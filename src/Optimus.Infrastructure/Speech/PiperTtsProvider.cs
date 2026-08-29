using System.Diagnostics;
using System.Runtime.Versioning;
using Optimus.Core.Abstractions;
using Optimus.Core.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Synthèse vocale neuronale locale, par Piper.
///
/// <b>Locale</b> au sens fort : le modèle tourne sur cette machine, rien ne part sur le réseau,
/// et Optimus reste utilisable hors ligne (§84). C'est ce qui distingue Piper d'un service de
/// synthèse en ligne, dont le timbre serait peut-être meilleur mais qui enverrait chaque phrase
/// du copilote à un tiers.
///
/// <b>Un processus persistant, et c'est mesuré</b> (2026-08-27, fr_FR-tom-medium) : charger la
/// voix coûte <b>0,55 s</b>, synthétiser une réplique <b>0,15 à 0,20 s</b>. Relancer
/// <c>piper.exe</c> à chaque phrase ferait donc payer 0,75 s avant le premier mot, contre 7 à
/// 15 ms pour les voix Windows — un échange que personne n'accepterait. Le processus reste ouvert,
/// la voix chargée, et <see cref="WarmUpAsync"/> paie le chargement au démarrage (D23).
///
/// Le protocole est celui qui a été <b>vérifié</b> : une ligne de texte sur l'entrée standard,
/// un chemin de fichier WAV sur la sortie standard, les journaux sur l'erreur standard. Le mode
/// <c>--json-input</c> a été essayé et écarté — il ignore <c>length_scale</c> dans cette version,
/// ce qui aurait rendu le débit inopérant sans que rien ne le signale.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class PiperTtsProvider : ITextToSpeechProvider
{
    /// <summary>
    /// Au-delà, on considère que Piper s'est bloqué.
    ///
    /// Généreux : une longue réplique sur une machine chargée peut dépasser la seconde. Mais
    /// borné, car une lecture sans limite sur un processus mort attendrait <b>pour toujours</b>,
    /// et le copilote se tairait sans que rien n'explique pourquoi.
    /// </summary>
    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Au-delà, on cesse d'attendre que la voix soit chargée et on tente quand même.
    ///
    /// Mesuré à 0,55 s pour un modèle « medium » ; dix secondes laissent la place à un modèle
    /// « high » sur une machine lente. Ce délai n'est pas une panne quand il expire : Piper a
    /// peut-être simplement changé le libellé qu'on guette, et une synthèse qui attend un peu
    /// vaut mieux qu'un copilote qui refuse de parler pour un mot de journal.
    /// </summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Ce que Piper écrit sur l'erreur standard quand la voix est en place.</summary>
    private const string ReadyMarker = "Initialized piper";

    private readonly PiperInstallation _installation;
    private readonly string _workDirectory;
    private readonly double _expectedRate;
    private readonly MediaPlayer _player = new();
    private readonly SemaphoreSlim _speaking = new(1, 1);

    private Process? _piper;
    private TaskCompletionSource? _ready;
    private string? _loadedVoice;
    private double _loadedRate = double.NaN;
    private bool _disposed;

    /// <param name="expectedRate">
    /// Débit auquel le copilote parlera. Il fait partie de l'identité du processus — Piper le
    /// lit en argument de ligne de commande — et le préchauffage doit donc démarrer <b>ce</b>
    /// processus-là. Le préchauffer à un autre débit reviendrait à payer le chargement deux
    /// fois : une fois pour rien, puis une seconde à la première réplique, ce qui est exactement
    /// ce que D23 cherchait à éviter.
    /// </param>
    public PiperTtsProvider(
        PiperInstallation installation,
        string? workDirectory = null,
        double expectedRate = 1.0)
    {
        ArgumentNullException.ThrowIfNull(installation);

        _installation = installation;
        _expectedRate = expectedRate;
        _workDirectory = workDirectory ?? Path.Combine(Path.GetTempPath(), "optimus-piper");

        Directory.CreateDirectory(_workDirectory);
        Sweep();
    }

    public string Id => "piper";

    public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_installation.Voices());

    public async Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Une ligne, et une seule. Piper decoupe son entree par saut de ligne : un texte sur deux
        // lignes deviendrait deux syntheses, et la seconde resterait dans le tuyau a decaler
        // toutes les repliques suivantes d'un cran. C'est le genre de defaut qui ne se voit
        // qu'au bout de dix commandes, quand Optimus repond a la precedente.
        string line = Flatten(request.Text);

        if (line.Length == 0)
        {
            return;
        }

        await _speaking.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Process piper = await EnsureRunningAsync(request.VoiceId, request.Rate, cancellationToken)
                .ConfigureAwait(false);

            string wave = await SynthesizeAsync(piper, line, cancellationToken).ConfigureAwait(false);

            try
            {
                await PlayAsync(wave, request.Volume, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Delete(wave);
            }
        }
        finally
        {
            _speaking.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _player.Pause();
        _player.Source = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Démarre le processus et charge la voix, pour que la première réplique ne paie pas les
    /// 0,55 s de chargement (D23).
    /// </summary>
    public async Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await EnsureRunningAsync(voiceId, _expectedRate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Garantit un processus vivant, chargé de la bonne voix au bon débit.
    ///
    /// Le débit fait partie de l'identité du processus parce que Piper le lit en argument de
    /// ligne de commande : le changer impose de relancer. Ce n'est pas grave — un débit se règle
    /// dans les réglages, pas entre deux phrases — mais il fallait le décider plutôt que le subir.
    /// </summary>
    private async Task<Process> EnsureRunningAsync(
        string? voiceId, double rate, CancellationToken cancellationToken)
    {
        string voice = voiceId ?? string.Empty;
        double scale = LengthScale(rate);

        bool usable = _piper is { HasExited: false }
                      && string.Equals(_loadedVoice, voice, StringComparison.Ordinal)
                      && Math.Abs(_loadedRate - scale) < 0.001;

        if (usable)
        {
            return _piper!;
        }

        Shutdown();

        string? model = _installation.ModelPath(voiceId)
            ?? throw new InvalidOperationException(
                $"No Piper voice named “{voiceId}” in {_installation.VoicesDirectory}.");

        ProcessStartInfo start = new(_installation.Executable)
        {
            WorkingDirectory = Path.GetDirectoryName(_installation.Executable)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("--model");
        start.ArgumentList.Add(model);
        start.ArgumentList.Add("--output_dir");
        start.ArgumentList.Add(_workDirectory);
        start.ArgumentList.Add("--length_scale");
        start.ArgumentList.Add(scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));

        Process piper = Process.Start(start)
            ?? throw new InvalidOperationException("Piper did not start.");

        // Les journaux de Piper partent sur l'erreur standard. Les lire en continu n'est pas un
        // luxe : un tuyau qu'on ne vide jamais finit par se remplir, et le processus se bloque
        // alors en ecrivant - panne d'autant plus deroutante qu'elle n'arrive qu'apres un moment.
        // C'est aussi la que Piper annonce que la voix est chargee.
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(() => DrainAsync(piper, ready), CancellationToken.None);

        _ready = ready;
        _piper = piper;
        _loadedVoice = voice;
        _loadedRate = scale;

        // Attendre que la voix soit chargee, ICI. Sans cette attente, le prechauffage ne ferait
        // que lancer le processus et la premiere replique paierait les 0,55 s de chargement en
        // plus de sa synthese : 740 ms mesures, exactement ce que D23 voulait eviter.
        long loading = System.Diagnostics.Stopwatch.GetTimestamp();
        bool loaded = await WaitReadyAsync(ready, cancellationToken).ConfigureAwait(false);

        DiagnosticLog.Info(
            "Piper started",
            $"{Path.GetFileNameWithoutExtension(model)} · rate ×{rate:F2} (length_scale {scale:0.###})"
            + $" · voice loaded in {Elapsed(loading):F0} ms"
            + (loaded ? string.Empty : " (announcement never received, trying anyway)"));

        return piper;
    }

    /// <summary>
    /// Envoie une ligne et attend le chemin du WAV produit.
    ///
    /// Piper annonce le chemin <b>même quand il n'a rien pu écrire</b> — un dossier absent
    /// n'arrache aucune erreur, seulement un fichier qui n'existe pas (constaté le 2026-08-27).
    /// L'existence est donc vérifiée avant de tenter la lecture.
    /// </summary>
    private async Task<string> SynthesizeAsync(
        Process piper, string line, CancellationToken cancellationToken)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        await piper.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await piper.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

        using CancellationTokenSource timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(SynthesisTimeout);

        string? path;

        try
        {
            path = await piper.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Shutdown();

            throw new InvalidOperationException(
                $"Piper produced nothing within {SynthesisTimeout.TotalSeconds:F0} s. Process restarted.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Shutdown();
            throw new InvalidOperationException("Piper stopped without producing anything.");
        }

        string wave = path.Trim();

        // La duree est journalisee a chaque replique, et pas seulement mesuree une fois ici : une
        // machine chargee, une voix « high » plutot que « medium », un antivirus qui inspecte
        // chaque WAV — le pilote doit pouvoir voir que sa synthese ralentit, et le constater sur
        // sa machine plutot que de se fier a la mienne.
        DiagnosticLog.Debug(
            "Piper synthesised",
            $"{Elapsed(start):F0} ms · {line.Length} characters");

        return File.Exists(wave)
            ? wave
            : throw new InvalidOperationException(
                $"Piper announces “{wave}”, which does not exist. Is the folder writable?");
    }

    private async Task PlayAsync(string wave, double volume, CancellationToken cancellationToken)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnEnded(MediaPlayer sender, object args) => completion.TrySetResult();
        void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
            completion.TrySetException(new InvalidOperationException(
                $"Lecture audio impossible : {args.ErrorMessage}"));

        _player.MediaEnded += OnEnded;
        _player.MediaFailed += OnFailed;

        try
        {
            // Piper n'a pas de reglage de volume : c'est le lecteur qui s'en charge.
            _player.Volume = Math.Clamp(volume, 0.0, 1.0);
            _player.Source = MediaSource.CreateFromUri(new Uri(wave));
            _player.Play();

            using CancellationTokenRegistration registration =
                cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _player.MediaEnded -= OnEnded;
            _player.MediaFailed -= OnFailed;

            // La source garde le fichier ouvert : la relacher avant d'essayer de l'effacer.
            _player.Source = null;
        }
    }

    /// <summary>
    /// Piper compte le temps en <b>longueur de phonème</b> : plus le nombre est grand, plus la
    /// parole est lente. C'est l'inverse de la convention d'Optimus, où 1,0 est le débit naturel
    /// et 1,5 va plus vite. L'inversion se fait ici, une fois, plutôt que dans chaque appelant.
    /// </summary>
    private static double Elapsed(long start) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - start)
        * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static double LengthScale(double rate) => 1.0 / Math.Clamp(rate, 0.25, 4.0);

    /// <summary>
    /// Ramène un texte à une seule ligne. Voir <see cref="SpeakAsync"/> pour ce qui arriverait
    /// sinon.
    /// </summary>
    private static string Flatten(string? text) => string.IsNullOrWhiteSpace(text)
        ? string.Empty
        : string.Join(' ', text.Split(
            ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Attend l'annonce de disponibilité, sans en faire une condition.
    ///
    /// Retourne <c>false</c> si l'annonce n'est pas venue — soit que le processus soit mort, soit
    /// que Piper ait changé son libellé. Dans les deux cas on continue : la synthèse suivante
    /// dira la vérité bien mieux qu'une supposition tirée d'une ligne de journal.
    /// </summary>
    private static async Task<bool> WaitReadyAsync(
        TaskCompletionSource ready, CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(LoadTimeout);

        try
        {
            await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task DrainAsync(Process piper, TaskCompletionSource ready)
    {
        try
        {
            while (await piper.StandardError.ReadLineAsync().ConfigureAwait(false) is string line)
            {
                if (line.Contains(ReadyMarker, StringComparison.OrdinalIgnoreCase))
                {
                    ready.TrySetResult();
                }

                if (line.Contains("[error]", StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticLog.Warn("Piper reports an error", line);
                }
            }
        }
        catch (Exception)
        {
            // Le processus s'est arrete : c'est la fin normale de cette boucle.
        }
        finally
        {
            // Debloquer une attente qui n'aura jamais sa reponse : un processus mort ne dira
            // plus rien, et laisser le prechauffage attendre ses dix secondes pour rien serait
            // dix secondes de demarrage offertes a une panne.
            ready.TrySetResult();
        }
    }

    private static void Delete(string wave)
    {
        try
        {
            File.Delete(wave);
        }
        catch (IOException)
        {
            // Un fichier temporaire qui survit une fois de trop n'est pas une panne ; le
            // nettoyage au demarrage le ramassera.
        }
    }

    /// <summary>Efface les WAV qu'un arrêt brutal aurait laissés derrière lui.</summary>
    private void Sweep()
    {
        try
        {
            foreach (string stale in Directory.EnumerateFiles(_workDirectory, "*.wav"))
            {
                Delete(stale);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Warn("could not clean up the Piper files", exception.Message);
        }
    }

    private void Shutdown()
    {
        if (_piper is not Process piper)
        {
            return;
        }

        _piper = null;
        _ready = null;
        _loadedVoice = null;
        _loadedRate = double.NaN;

        try
        {
            if (!piper.HasExited)
            {
                piper.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Deja mort, ou hors de portee : rien de plus a tenter.
        }
        finally
        {
            piper.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        Shutdown();
        _player.Dispose();
        _speaking.Dispose();
        Sweep();

        return ValueTask.CompletedTask;
    }
}
