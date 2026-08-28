using System.Diagnostics;
using System.Globalization;
using System.Text;
using Optimus.Core.Diagnostics;
using Optimus.Core.Speech;

namespace Optimus.Infrastructure.Speech;

/// <summary>Une installation de whisper.cpp trouvée sur la machine.</summary>
/// <param name="Executable">Chemin complet de <c>whisper-cli.exe</c>.</param>
/// <param name="ModelsDirectory">Dossier des modèles <c>ggml-*.bin</c>.</param>
public sealed record WhisperInstallation(string Executable, string ModelsDirectory)
{
    /// <summary>Dossier attendu, sous les données du pilote.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Optimus",
        "whisper");

    /// <summary>
    /// Cherche une installation utilisable, ou rend <c>null</c>.
    ///
    /// Exige le binaire <b>et</b> au moins un modèle, pour la même raison que Piper (D57) : une
    /// installation à moitié faite rendrait Optimus silencieux sur la parole libre sans que rien
    /// n'explique pourquoi.
    /// </summary>
    public static WhisperInstallation? Locate(string? root = null)
    {
        string directory = root ?? DefaultRoot;
        string executable = Path.Combine(directory, "whisper-cli.exe");

        if (!File.Exists(executable))
        {
            return null;
        }

        string models = Path.Combine(directory, "models");

        if (!Directory.Exists(models) || Directory.GetFiles(models, "ggml-*.bin").Length == 0)
        {
            return null;
        }

        return new WhisperInstallation(executable, models);
    }

    /// <summary>Modèles installés, par leur nom court : <c>base</c>, <c>small</c>…</summary>
    public IReadOnlyList<string> Models() =>
        Directory.EnumerateFiles(ModelsDirectory, "ggml-*.bin")
            .Select(f => Path.GetFileNameWithoutExtension(f)["ggml-".Length..])
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Chemin du modèle demandé, ou le premier installé.</summary>
    public string? ModelPath(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            string direct = Path.Combine(ModelsDirectory, $"ggml-{name}.bin");

            if (File.Exists(direct))
            {
                return direct;
            }
        }

        return Directory.EnumerateFiles(ModelsDirectory, "ggml-*.bin")
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

/// <summary>Ce qu'une transcription a produit.</summary>
/// <param name="Text">Texte prononcé, ou vide si rien n'a été compris.</param>
/// <param name="ElapsedMs">Durée totale, pour que le pilote voie ce que ça lui coûte.</param>
public sealed record Transcription(string Text, double ElapsedMs)
{
    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public static Transcription Empty { get; } = new(string.Empty, 0);
}

/// <summary>
/// L'étage de parole libre, par whisper.cpp.
///
/// <b>Il n'ouvre jamais de micro.</b> Il transcrit l'audio que le moteur rapide lui tend —
/// <c>RecognitionResult.Audio</c>, mesuré récupérable en S0-7, et rendu en 16 kHz 16 bits mono,
/// exactement le format attendu. Seuls les énoncés qui ont déjà déclenché le moteur passent donc
/// par ici : ce que le pilote dit à côté n'est jamais transcrit, et la promesse de l'écran de
/// réglages tient.
///
/// <b>Un processus par énoncé, contrairement à Piper.</b> Le patron persistant de D55 n'est pas
/// repris, et c'est un choix mesuré, pas un oubli : chez Piper le chargement pesait 0,6 s pour
/// 0,2 s de synthèse — il dominait. Ici il pèse <b>167 ms pour 900</b>, soit 15 %. Tenir un
/// processus ouvert, sa santé, son redémarrage et son tuyau pour gagner un sixième ne valait pas
/// la complexité. <c>whisper-server.exe</c> est livré dans la même archive et reste la voie de
/// secours le jour où ces 167 ms compteront.
/// </summary>
public sealed class WhisperTranscriber
{
    /// <summary>
    /// Au-delà, on abandonne.
    ///
    /// Généreux : trente secondes couvrent une machine chargée et un modèle plus gros. Mais
    /// borné, car une attente sans limite laisserait le pilote sans réponse et sans explication.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly WhisperInstallation _installation;
    private readonly WhisperSettings _settings;
    private readonly string _model;

    public WhisperTranscriber(WhisperInstallation installation, WhisperSettings settings)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(settings);

        _installation = installation;
        _settings = settings;

        _model = installation.ModelPath(settings.Model)
            ?? throw new InvalidOperationException(
                $"Aucun modèle Whisper dans {installation.ModelsDirectory}.");
    }

    /// <summary>Nom du modèle réellement chargé.</summary>
    public string ModelName => Path.GetFileNameWithoutExtension(_model)["ggml-".Length..];

    /// <summary>
    /// Transcrit un fichier WAV. Ne lève jamais : un étage facultatif ne fait rien tomber.
    /// </summary>
    public async Task<Transcription> TranscribeAsync(
        string wavePath, string language = "fr", CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavePath);

        if (!File.Exists(wavePath))
        {
            DiagnosticLog.Warn("audio à transcrire introuvable", wavePath);
            return Transcription.Empty;
        }

        long start = Stopwatch.GetTimestamp();

        ProcessStartInfo startInfo = new(_installation.Executable)
        {
            WorkingDirectory = Path.GetDirectoryName(_installation.Executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in Arguments(wavePath, language))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process? process = Process.Start(startInfo);

            if (process is null)
            {
                DiagnosticLog.Warn("Whisper n'a pas démarré", _installation.Executable);
                return Transcription.Empty;
            }

            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(Timeout);

            // Les deux tuyaux se lisent EN MEME TEMPS que l'attente : whisper ecrit ses journaux
            // sur l'erreur standard, et un tuyau qu'on ne vide pas finit par bloquer le processus
            // qui ecrit dedans — panne d'autant plus deroutante qu'elle n'arrive qu'au-dela d'une
            // certaine longueur de sortie.
            Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string transcript = Clean(await output.ConfigureAwait(false));

            if (process.ExitCode != 0)
            {
                DiagnosticLog.Warn(
                    $"Whisper a rendu {process.ExitCode}",
                    Tail(await errors.ConfigureAwait(false)));

                return Transcription.Empty;
            }

            double elapsed = Elapsed(start);

            DiagnosticLog.Debug(
                "Whisper a transcrit",
                $"{elapsed:F0} ms · {ModelName} · {_settings.EffectiveThreads} fils · "
                + $"« {transcript} »");

            return new Transcription(transcript, elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            DiagnosticLog.Warn(
                $"Whisper n'a pas répondu en {Timeout.TotalSeconds:F0} s",
                "l'énoncé est abandonné, Optimus reste utilisable");

            return Transcription.Empty;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Warn("transcription impossible", exception.Message);
            return Transcription.Empty;
        }
    }

    private IEnumerable<string> Arguments(string wavePath, string language)
    {
        yield return "--model";
        yield return _model;

        yield return "--file";
        yield return wavePath;

        yield return "--language";
        yield return language;

        yield return "--threads";
        yield return _settings.EffectiveThreads.ToString(CultureInfo.InvariantCulture);

        // Sans horodatage ni couleur : on veut du texte, pas une transcription mise en forme.
        yield return "--no-timestamps";

        // Decodage glouton. La recherche en faisceau par defaut — cinq branches — coute une
        // centaine de millisecondes pour un gain nul sur des enonces de quelques mots.
        yield return "--best-of";
        yield return "1";

        yield return "--beam-size";
        yield return "1";

        if (_settings.AudioContext > 0)
        {
            yield return "--audio-context";
            yield return _settings.AudioContext.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Réduit la sortie de whisper.cpp au texte prononcé.
    ///
    /// Le binaire écrit la transcription sur la sortie standard, précédée d'un espace, et
    /// parfois vide. Les marqueurs entre crochets — <c>[BLANK_AUDIO]</c>, <c>[MUSIC]</c> — ne
    /// sont pas de la parole : les laisser passer ferait chercher au rapprochement flou une
    /// commande nommée « blank audio ».
    /// </summary>
    private static string Clean(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        StringBuilder text = new();

        foreach (string line in raw.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0
                || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(trimmed);
        }

        return text.ToString().Trim();
    }

    /// <summary>Dernières lignes d'une sortie d'erreur, pour un journal lisible.</summary>
    private static string Tail(string raw) =>
        string.Join(
            " · ",
            raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(3));

    private static double Elapsed(long start) =>
        (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
}
