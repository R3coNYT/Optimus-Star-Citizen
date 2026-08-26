using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Optimus.Core.Diagnostics;

/// <summary>Gravité d'une ligne de journal.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Journal de diagnostic et rapport de plantage.
///
/// Optimus tourne sur une machine qui n'est pas celle où il est développé, pendant une session
/// de jeu, en plein écran. Quand il tombe, il n'y a ni débogueur, ni console, ni fenêtre — il
/// disparaît, et c'est tout ce que l'on sait. Ce fichier est la seule chose qui reste.
///
/// Deux garanties comptent ici et dictent tout le reste :
///
/// <list type="number">
/// <item><b>Chaque ligne est écrite immédiatement.</b> Un tampon vidé « plus tard » n'existe
/// pas quand le processus meurt à la ligne suivante, et c'est précisément celle-là qui
/// intéresse.</item>
/// <item><b>Journaliser ne peut jamais faire tomber le programme.</b> Un disque plein ou un
/// fichier verrouillé donnerait sinon un plantage de plus, provoqué par l'outil censé
/// l'expliquer.</item>
/// </list>
///
/// Le rapport de plantage emporte aussi les dernières lignes précédant la chute : savoir
/// <i>ce qui se passait</i> vaut souvent mieux que la pile d'appels.
/// </summary>
public static class DiagnosticLog
{
    /// <summary>Lignes conservées en mémoire, jointes au rapport de plantage.</summary>
    private const int TrailSize = 200;

    /// <summary>
    /// Longueur maximale d'un détail.
    ///
    /// Une pile d'appels WPF depasse aisement cent mille caracteres, et la reproduire dans la
    /// trace fait grossir chaque rapport suivant du precedent : quatre plantages d'affilee ont
    /// produit des fichiers de 79 puis 315 Ko, illisibles et sans information de plus.
    /// </summary>
    private const int DetailLimit = 4000;

    /// <summary>Journaux plus vieux que cela : effacés au démarrage.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(14);

    private static readonly object Gate = new();
    private static readonly Queue<string> Trail = new();

    private static string? _file;
    private static string? _component;

    /// <summary>Dossier des journaux, dans les données de l'utilisateur.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Optimus", "logs");

    /// <summary>Fichier du jour, ou <c>null</c> tant que rien n'a démarré.</summary>
    public static string? CurrentFile => _file;

    /// <summary>
    /// Ouvre le journal du jour et purge les anciens.
    ///
    /// Un fichier par jour et par composant : l'application et le banc d'essai peuvent tourner
    /// en même temps sans se marcher dessus.
    /// </summary>
    public static void Start(string component, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);

        lock (Gate)
        {
            _component = component;

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                _file = Path.Combine(
                    Directory,
                    $"{component}-{DateTime.Now:yyyy-MM-dd}.log");

                Purge();
            }
            catch (Exception)
            {
                // Sans journal, on continue quand meme : perdre la trace est facheux,
                // ne pas demarrer serait pire.
                _file = null;
            }
        }

        Info($"=== {component} démarre ===",
            $"version {version ?? "inconnue"} · {RuntimeInformation()}");
    }

    public static void Debug(string message, string? detail = null) => Write(LogLevel.Debug, message, detail);

    public static void Info(string message, string? detail = null) => Write(LogLevel.Info, message, detail);

    public static void Warn(string message, string? detail = null) => Write(LogLevel.Warn, message, detail);

    public static void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, message, exception is null ? null : Describe(exception));

    /// <summary>
    /// Écrit un rapport de plantage complet et retourne son chemin.
    ///
    /// Un fichier distinct du journal courant : on doit pouvoir l'envoyer tel quel sans avoir à
    /// retrouver le bon passage dans des heures de trace.
    /// </summary>
    public static string? WriteCrashReport(Exception exception, string origin, string? extra = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Error($"PLANTAGE ({origin})", exception);

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            string path = Path.Combine(
                Directory,
                $"plantage-{DateTime.Now:yyyy-MM-dd-HHmmss}.txt");

            StringBuilder report = new();
            report.AppendLine("RAPPORT DE PLANTAGE OPTIMUS");
            report.AppendLine("===========================");
            report.AppendLine();
            report.AppendLine($"Composant   : {_component ?? "inconnu"}");
            report.AppendLine($"Date        : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            report.AppendLine($"Origine     : {origin}");
            report.AppendLine($"Environnement : {RuntimeInformation()}");

            if (extra is not null)
            {
                report.AppendLine();
                report.AppendLine("CONTEXTE");
                report.AppendLine("--------");
                report.AppendLine(extra);
            }

            report.AppendLine();
            report.AppendLine("EXCEPTION");
            report.AppendLine("---------");
            report.AppendLine(Describe(exception));

            report.AppendLine();
            report.AppendLine($"DERNIÈRES LIGNES AVANT LA CHUTE ({Trail.Count})");
            report.AppendLine("--------------------------------");

            lock (Gate)
            {
                foreach (string line in Trail)
                {
                    report.AppendLine(line);
                }
            }

            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            return path;
        }
        catch (Exception)
        {
            // On est deja en train de tomber : echouer ici ne doit rien aggraver.
            return null;
        }
    }

    /// <summary>Ouvre le dossier des journaux dans l'explorateur.</summary>
    public static void Reveal()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            Process.Start(new ProcessStartInfo(Directory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Warn("impossible d'ouvrir le dossier des journaux", exception.Message);
        }
    }

    private static void Write(LogLevel level, string message, string? detail)
    {
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:HH:mm:ss.fff} {Label(level)} {message}");

        if (detail is not null)
        {
            string trimmed = detail.Length > DetailLimit
                ? detail[..DetailLimit] + $"{Environment.NewLine}… ({detail.Length - DetailLimit} caractères de plus)"
                : detail;

            line += Environment.NewLine + "    " + trimmed.Replace(
                Environment.NewLine, Environment.NewLine + "    ", StringComparison.Ordinal);
        }

        lock (Gate)
        {
            Trail.Enqueue(line);

            while (Trail.Count > TrailSize)
            {
                Trail.Dequeue();
            }

            if (_file is null)
            {
                return;
            }

            try
            {
                // Ajout immediat, sans tampon : la ligne qui compte est toujours la derniere.
                File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                // Disque plein, fichier verrouille, dossier efface pendant la session : rien de
                // tout cela ne justifie d'abattre Optimus.
            }
        }
    }

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Debug => "[debug]",
        LogLevel.Warn => "[ATTENTION]",
        LogLevel.Error => "[ERREUR]",
        _ => "[info] ",
    };

    /// <summary>Déroule la chaîne des exceptions internes, souvent plus parlante que la première.</summary>
    private static string Describe(Exception exception)
    {
        StringBuilder builder = new();
        Exception? current = exception;
        int depth = 0;

        while (current is not null && depth < 8)
        {
            builder.AppendLine($"{(depth == 0 ? string.Empty : "→ causée par ")}{current.GetType().FullName} : {current.Message}");

            if (current.StackTrace is string stack)
            {
                builder.AppendLine(stack);
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString().TrimEnd();
    }

    private static string RuntimeInformation() =>
        $"{Environment.OSVersion.VersionString} · .NET {Environment.Version} · "
        + $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} · "
        + $"culture {CultureInfo.CurrentCulture.Name} · {Environment.ProcessorCount} processeurs";

    private static void Purge()
    {
        DateTime limit = DateTime.Now - Retention;

        foreach (string file in System.IO.Directory.EnumerateFiles(Directory))
        {
            try
            {
                if (File.GetLastWriteTime(file) < limit)
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
                // Un fichier retenu par un autre processus reste : ce n'est pas grave.
            }
        }
    }
}
