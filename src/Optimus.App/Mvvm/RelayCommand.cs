using System.Windows.Input;

namespace Optimus.App.Mvvm;

/// <summary>Commande déléguée, synchrone.</summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Commande déléguée asynchrone.
///
/// Se ré-entre pas : tant que l'opération court, la commande se refuse. Un double clic sur
/// « Écouter » ne doit pas ouvrir deux fois le micro.
/// </summary>
public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _running;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();

        try
        {
            await execute().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // Une exception dans un « async void » abattrait l'application. On la montre.
            System.Windows.MessageBox.Show(
                exception.Message, "Optimus", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
