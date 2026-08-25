using System.Windows;
using System.Windows.Threading;
using Optimus.Infrastructure.Hosting;

namespace Optimus.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Une exception non gérée sur le fil d'interface fermerait la fenêtre sans un mot.
        // Optimus doit survivre à une commande ratée : on montre, on continue.
        DispatcherUnhandledException += OnUnhandled;

        string? dataRoot = OptimusRuntime.FindDataRoot(AppContext.BaseDirectory);

        if (dataRoot is null)
        {
            MessageBox.Show(
                "Dossier « data » introuvable au-dessus de l'exécutable.\n\n"
                + "Optimus a besoin du catalogue de commandes et du profil de bindings pour démarrer.",
                "Optimus", MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        MainWindow window = new(OptimusRuntime.Load(dataRoot));
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message, "Optimus — erreur inattendue",
            MessageBoxButton.OK, MessageBoxImage.Warning);

        e.Handled = true;
    }
}
