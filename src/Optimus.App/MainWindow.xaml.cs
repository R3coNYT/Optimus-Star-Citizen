using System.Collections.Specialized;
using System.Windows;
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

    public MainWindow(OptimusRuntime runtime)
    {
        InitializeComponent();

        _model = new MainViewModel(runtime) { Owner = this };
        DataContext = _model;

        ((INotifyCollectionChanged)_model.Journal).CollectionChanged += OnJournalChanged;

        Loaded += async (_, _) => await _model.WarmUpAsync();
        Closed += async (_, _) => await _model.DisposeAsync();
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
            DiagnosticLog.Warn("défilement du journal impossible", exception.Message);
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
