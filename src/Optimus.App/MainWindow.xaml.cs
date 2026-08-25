using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Optimus.App.ViewModels;
using Optimus.Infrastructure.Hosting;

namespace Optimus.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow(OptimusRuntime runtime)
    {
        InitializeComponent();

        _model = new MainViewModel(runtime) { Owner = this };
        DataContext = _model;

        // Le journal defile tout seul : suivre a la main pendant une session de vol serait
        // intenable, et c'est justement la que l'on regarde le moins l'ecran.
        ((INotifyCollectionChanged)_model.Journal).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && JournalList.Items.Count > 0)
            {
                JournalList.ScrollIntoView(JournalList.Items[^1]);
            }
        };

        Loaded += async (_, _) => await _model.WarmUpAsync();
        Closed += async (_, _) => await _model.DisposeAsync();
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
