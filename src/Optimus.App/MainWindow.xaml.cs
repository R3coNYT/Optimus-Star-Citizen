using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Optimus.App.ViewModels;
using Optimus.Core.Diagnostics;
using Optimus.Infrastructure.Hosting;

namespace Optimus.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private bool _scrollPending;

    private TrayIcon? _tray;
    private ContextMenu? _menu;

    /// <summary>Vrai quand la fermeture est VOULUE, et non un simple repli dans le plateau.</summary>
    private bool _quitting;

    /// <summary>La bulle n'est dite qu'une fois par session : la deuxième serait du bruit.</summary>
    private bool _warned;

    public MainWindow(OptimusRuntime runtime)
    {
        InitializeComponent();

        _model = new MainViewModel(runtime) { Owner = this };
        DataContext = _model;

        ((INotifyCollectionChanged)_model.Journal).CollectionChanged += OnJournalChanged;

        SourceInitialized += (_, _) =>
        {
            PaintTitleBarDark();
            InstallTray();
        };

        Loaded += async (_, _) => await _model.WarmUpAsync();

        Closed += async (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;

            await _model.DisposeAsync();
        };
    }

    // ------------------------------------------------------------------ la zone de notification

    /// <summary>
    /// Pose l'icône, et branche ses deux gestes.
    ///
    /// Après <c>SourceInitialized</c> parce que le menu a besoin du handle de cette fenêtre, et
    /// que l'icône a besoin d'exister avant la première fermeture.
    ///
    /// Un échec n'empêche rien : Optimus reste utilisable, la fenêtre se ferme pour de bon comme
    /// avant. Perdre l'icône ne doit pas coûter l'application.
    /// </summary>
    private void InstallTray()
    {
        try
        {
            _tray = new TrayIcon(Localization.Localizer.T("Tray.Idle"));
            _tray.Opened += (_, _) => Dispatcher.Invoke(Restore);
            _tray.MenuRequested += (_, _) => Dispatcher.Invoke(ShowTrayMenu);

            _model.PropertyChanged += (_, _) => Dispatcher.Invoke(RefreshTooltip);
            RefreshTooltip();
        }
        catch (Exception exception)
        {
            DiagnosticLog.Warn("notification icon unavailable", exception.Message);
            _tray = null;
        }
    }

    /// <summary>
    /// Le menu du plateau, bâti sur les MÊMES commandes que la fenêtre.
    ///
    /// Rien n'est redéfini ici : « Cockpit.Listen » devient « Cockpit.StopListening » au même
    /// moment des deux côtés, parce que c'est la même propriété qui l'écrit. Deux menus tenus
    /// séparément auraient fini par se contredire, et c'est précisément dans le plateau qu'un
    /// libellé faux se remarque le plus tard.
    /// </summary>
    private void ShowTrayMenu()
    {
        _menu ??= BuildTrayMenu();

        // Passer au premier plan AVANT d'ouvrir : sans cela le menu reste affiché quand on
        // clique ailleurs, et il faut le rappeler pour s'en débarrasser. La fenêtre peut être
        // masquée, son handle existe tant qu'elle n'est pas fermée.
        SetForegroundWindow(new WindowInteropHelper(this).Handle);

        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private ContextMenu BuildTrayMenu()
    {
        ContextMenu menu = new() { DataContext = _model };

        MenuItem open = new() { Header = Localization.Localizer.T("Tray.Open"), FontWeight = FontWeights.Bold };
        open.Click += (_, _) => Restore();
        menu.Items.Add(open);

        menu.Items.Add(new Separator());

        menu.Items.Add(Bound("ListeningLabel", "ToggleListeningCommand"));
        menu.Items.Add(Bound("ModeLabel", "ToggleSimulationCommand"));
        menu.Items.Add(Bound("KillSwitchLabel", "ToggleKillSwitchCommand"));

        menu.Items.Add(new Separator());

        MenuItem quit = new() { Header = Localization.Localizer.T("Tray.Quit") };
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);

        return menu;
    }

    /// <summary>Un élément dont le libellé et l'action viennent du modèle, comme à l'écran.</summary>
    private static MenuItem Bound(string label, string command)
    {
        MenuItem item = new();

        item.SetBinding(HeaderedItemsControl.HeaderProperty, new System.Windows.Data.Binding(label));
        item.SetBinding(MenuItem.CommandProperty, new System.Windows.Data.Binding(command));

        return item;
    }

    /// <summary>
    /// Ce que dit l'infobulle : l'arrêt d'urgence d'abord, l'écoute ensuite.
    ///
    /// Dans cet ordre parce que c'est celui de la gravité. Un arrêt d'urgence engagé explique
    /// pourquoi plus rien ne répond ; le savoir en survolant une icône évite de chercher
    /// ailleurs.
    /// </summary>
    private void RefreshTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        string key =
            _model.KillSwitch ? "Tray.Stopped"
            : _model.IsListening ? "Tray.Listening"
            : "Tray.Idle";

        _tray.SetTooltip(Localization.Localizer.T(key));
    }

    /// <summary>Remonte la fenêtre, même réduite, même derrière le jeu.</summary>
    private void Restore()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Fermer pour de bon, par le menu du plateau.</summary>
    private void Quit()
    {
        _quitting = true;
        Close();
    }

    /// <summary>
    /// Fermer la fenêtre ne ferme plus Optimus.
    ///
    /// C'est ce que demandent le Stream Deck, Discord et l'écoute vocale : aucun ne survit à
    /// l'arrêt du processus, et personne ne garde une fenêtre ouverte pendant qu'il joue.
    ///
    /// Mais un programme qui peut APPUYER SUR DES TOUCHES n'a pas le droit de survivre en
    /// silence à sa propre fermeture. D'où la bulle, dite une fois par session : elle nomme
    /// l'endroit où le trouver, et la façon de l'arrêter pour de bon.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_quitting || _tray is null)
        {
            return;
        }

        e.Cancel = true;
        Hide();

        if (_warned)
        {
            return;
        }

        _warned = true;

        _tray.ShowBalloon(
            Localization.Localizer.T("Tray.HiddenTitle"),
            Localization.Localizer.T("Tray.HiddenBody"));

        DiagnosticLog.Info("window hidden", "Optimus keeps running in the notification area");
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    /// <summary>Attribut DWM qui bascule la barre de titre en sombre (Windows 10 20H1 et suite).</summary>
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    /// <summary>
    /// Assombrit la barre de titre, que WPF laisse au thème du système.
    ///
    /// Sans cela, une bande blanche coiffe une fenêtre entièrement noire : c'est la seule
    /// chose éblouissante de l'écran, et elle casse net l'illusion d'un instrument de bord.
    /// WPF n'expose rien pour cela — la barre appartient au gestionnaire de fenêtres, pas à
    /// l'application — d'où cet appel au seul endroit où le handle existe déjà, mais où la
    /// fenêtre n'est pas encore peinte.
    ///
    /// L'échec est sans conséquence et donc silencieux : sur un Windows plus ancien que
    /// 20H1, l'attribut n'existe pas et la barre reste claire. Rien d'autre ne change.
    /// </summary>
    private void PaintTitleBarDark()
    {
        try
        {
            nint handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;

            DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // Pas de dwmapi : la barre reste celle du système, et c'est tout.
        }
    }

    /// <summary>
    /// Fait suivre le journal, <b>après</b> que WPF a fini de digérer le changement.
    ///
    /// La première version appelait <c>ScrollIntoView</c> directement ici. Cela force une passe
    /// de mise en page alors que la liste traite encore la notification : son générateur de
    /// conteneurs n'a pas rattrapé la collection, et il lève « ItemsControl incohérent par
    /// rapport à la source de ses éléments ». Une seule reconnaissance ajoute trois lignes en
    /// six millisecondes — de quoi déclencher la course à tous les coups, et l'application
    /// tombait dès la première commande.
    ///
    /// Le report en priorité <c>Background</c> attend que la mise en page soit stable, et le
    /// drapeau empêche d'empiler un défilement par ligne ajoutée.
    /// </summary>
    private void OnJournalChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _scrollPending)
        {
            return;
        }

        _scrollPending = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _scrollPending = false;
            ScrollJournalToEnd();
        });
    }

    /// <summary>
    /// Défile par le <c>ScrollViewer</c> plutôt que par <c>ScrollIntoView</c>.
    ///
    /// <c>ScrollIntoView</c> réalise l'élément visé, ce qui rappelle le générateur ; un simple
    /// <c>ScrollToEnd</c> ne fait que déplacer la fenêtre visible et ne peut pas le désynchroniser.
    /// </summary>
    private void ScrollJournalToEnd()
    {
        try
        {
            if (FindScrollViewer(JournalList) is ScrollViewer viewer)
            {
                viewer.ScrollToEnd();
            }
        }
        catch (Exception exception)
        {
            // Le confort de lecture ne vaut pas qu'Optimus tombe. C'est exactement ce qui
            // s'était produit, et une seule fois suffit.
            DiagnosticLog.Warn("could not scroll the log", exception.Message);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found)
        {
            return found;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is ScrollViewer viewer)
            {
                return viewer;
            }
        }

        return null;
    }

    private void OnUtteranceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        // Entrée envoie : taper une phrase puis viser un bouton casserait le rythme.
        if (_model.SendCommand.CanExecute(null))
        {
            _model.SendCommand.Execute(null);
        }

        e.Handled = true;
    }
}
