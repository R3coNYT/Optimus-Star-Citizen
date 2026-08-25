using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Optimus.App.Mvvm;

/// <summary>
/// Base de notification.
///
/// Écrite à la main plutôt qu'empruntée à une bibliothèque : quinze lignes contre une
/// dépendance, et §70 demande explicitement de ne pas sur-concevoir.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>Affecte et notifie, si la valeur change vraiment.</summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(property);
        return true;
    }
}
