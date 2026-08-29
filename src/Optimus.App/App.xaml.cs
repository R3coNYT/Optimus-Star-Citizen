using System.IO;
using System.Windows;
using System.Windows.Threading;
using Optimus.Core.Diagnostics;
using Optimus.Infrastructure.Hosting;

namespace Optimus.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DiagnosticLog.Start("Optimus.App", Version());
        InstallSafetyNets();

        // Verifie le filet lui-meme. Un dispositif de rapport de plantage qu'on n'a jamais vu
        // fonctionner n'est pas un dispositif : c'est une intention.
        if (e.Args.Contains("--test-crash", StringComparer.OrdinalIgnoreCase))
        {
            DiagnosticLog.Warn("essai de plantage demandé", "l'exception suivante est volontaire");

            // Un vrai fil, pas un Task.Run : une tache non observee ne tue pas le processus et
            // n'eprouverait donc pas le filet qui compte - celui du fil d'arriere-plan fatal.
            System.Threading.Thread thread = new(() => throw new InvalidOperationException(
                "Plantage d'essai déclenché par --test-crash. Si vous lisez ceci dans un rapport, "
                + "le dispositif fonctionne."))
            {
                IsBackground = false,
            };

            thread.Start();
            return;
        }

        try
        {
            DiagnosticLog.Info("recherche du dossier de données", AppContext.BaseDirectory);
            string? dataRoot = OptimusRuntime.FindDataRoot(AppContext.BaseDirectory);

            if (dataRoot is null)
            {
                DiagnosticLog.Error("dossier « data » introuvable");

                MessageBox.Show(
                    "Dossier « data » introuvable au-dessus de l'exécutable.\n\n"
                    + "Optimus a besoin du catalogue de commandes et du profil de bindings pour démarrer.",
                    "Optimus", MessageBoxButton.OK, MessageBoxImage.Error);

                Shutdown(1);
                return;
            }

            DiagnosticLog.Info("chargement des données", dataRoot);
            OptimusRuntime runtime = OptimusRuntime.Load(dataRoot);

            // Avant la fenetre : sans cela elle se peindrait en francais, puis se corrigerait
            // sous les yeux du pilote.
            Localization.Localizer.Apply(runtime.User.Language);

            DiagnosticLog.Info(
                "données chargées",
                $"{runtime.Catalog.Count} commandes · {runtime.Bindings.BoundCount} actions liées · "
                + $"copilote « {runtime.Copilot.Name} » · langue {runtime.User.Language}"
                + $" · voix {runtime.Copilot.Voice.VoiceId ?? "par défaut du système"}");

            foreach (Core.Loading.LoadIssue issue in runtime.Issues)
            {
                DiagnosticLog.Warn("anomalie de chargement", issue.ToString());
            }

            DiagnosticLog.Info("ouverture de la fenêtre");
            MainWindow window = new(runtime);
            MainWindow = window;
            window.Show();

            DiagnosticLog.Info("fenêtre affichée");
        }
        catch (Exception exception)
        {
            // Un plantage AU DEMARRAGE ne laisse aucune fenetre pour se plaindre : sans ce
            // filet, Optimus disparaitrait en silence avant d'avoir rien pu dire.
            Fatal(exception, "démarrage");
            Shutdown(1);
        }
    }

    /// <summary>
    /// Trois filets, parce qu'une exception ne remonte pas au même endroit selon le fil qui la
    /// lève, et que le plus dangereux des trois est celui qui tue le processus sans un mot.
    /// </summary>
    private void InstallSafetyNets()
    {
        // Fil d'interface : rattrapable. On montre, on continue.
        DispatcherUnhandledException += (_, args) =>
        {
            Fatal(args.Exception, "interface");
            args.Handled = true;
        };

        // N'importe quel autre fil : le CLR abat le processus juste apres ce gestionnaire.
        // On ne peut rien empecher, seulement laisser une trace - c'est tout l'objet.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Fatal(exception, args.IsTerminating ? "fil d'arrière-plan (fatal)" : "fil d'arrière-plan");
            }
        };

        // Tache dont personne n'a observe le resultat : silencieux par defaut depuis .NET 4.5,
        // donc invisible sans ceci.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticLog.Error("tâche non observée", args.Exception);
            args.SetObserved();
        };
    }

    private static string? _lastSignature;
    private static DateTimeOffset _lastCrash;
    private static int _repeats;

    /// <summary>
    /// Rapporte un plantage, sans s'emballer.
    ///
    /// Une exception levée pendant la mise en page se reproduit à chaque tentative de dessin :
    /// quatre rapports ont été écrits en quatre secondes, chacun contenant la pile du précédent.
    /// Le dispositif censé expliquer la chute devenait alors le problème. Une même chute
    /// répétée n'est donc rapportée qu'une fois, puis seulement comptée.
    /// </summary>
    private static void Fatal(Exception exception, string origin)
    {
        string signature = $"{exception.GetType().FullName}|{origin}|{FirstFrame(exception)}";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (signature == _lastSignature && now - _lastCrash < TimeSpan.FromSeconds(30))
        {
            _repeats++;
            DiagnosticLog.Warn(
                $"même plantage répété ({_repeats} fois)",
                "rapport déjà écrit, on ne le réécrit pas");
            return;
        }

        _lastSignature = signature;
        _lastCrash = now;
        _repeats = 1;

        string? report = DiagnosticLog.WriteCrashReport(exception, origin, Context());

        string message = $"Optimus a rencontré un problème ({origin}).\n\n{exception.Message}";

        if (report is not null)
        {
            message += $"\n\nRapport écrit dans :\n{report}\n\nOuvrir le dossier ?";

            if (MessageBox.Show(message, "Optimus", MessageBoxButton.YesNo, MessageBoxImage.Error)
                == MessageBoxResult.Yes)
            {
                DiagnosticLog.Reveal();
            }

            return;
        }

        MessageBox.Show(message, "Optimus", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Première ligne de pile applicative : ce qui distingue deux plantages différents.</summary>
    private static string FirstFrame(Exception exception) =>
        exception.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("Optimus", StringComparison.Ordinal))
        ?? exception.StackTrace?.Split(Environment.NewLine).FirstOrDefault()
        ?? string.Empty;

    /// <summary>Ce qu'il faut savoir de l'installation pour comprendre un rapport reçu par message.</summary>
    private static string Context()
    {
        string version = Path.Combine(AppContext.BaseDirectory, "VERSION.txt");

        return $"Exécutable  : {AppContext.BaseDirectory}\n"
             + $"Journaux    : {DiagnosticLog.Directory}\n"
             + $"VERSION.txt : {(File.Exists(version) ? File.ReadAllText(version).Trim().Replace("\n", " | ") : "absent")}";
    }

    private static string Version()
    {
        System.Reflection.Assembly assembly = typeof(App).Assembly;
        string number = assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        try
        {
            // Le chemin du processus, et non celui de l'assembly : publie en fichier unique,
            // `Assembly.Location` rend une chaine vide (IL3000), et le repere de date
            // disparaitrait justement dans la version qu'on distribue.
            string? executable = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(executable) && File.Exists(executable))
            {
                return $"{number} (compilé le {File.GetLastWriteTime(executable):yyyy-MM-dd HH:mm})";
            }
        }
        catch (IOException)
        {
            // Le repere est un confort.
        }

        return number;
    }
}
