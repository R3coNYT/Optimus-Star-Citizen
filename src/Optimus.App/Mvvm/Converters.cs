using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Optimus.Core.Domain.Commands;

namespace Optimus.App.Mvvm;

/// <summary>Vrai → visible, faux → replié. Replié plutôt que caché : la place doit se libérer.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// Sens d'une étape ↔ position dans une liste déroulante.
///
/// L'ordre des entrées porte le sens : basculer, allumer, éteindre. Le lier par index plutôt
/// que par valeur évite d'exposer un type du domaine dans le XAML, où une faute de frappe ne se
/// verrait qu'à l'exécution.
/// </summary>
public sealed class PolarityToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            CommandPolarity.On => 1,
            CommandPolarity.Off => 2,
            _ => 0,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            1 => CommandPolarity.On,
            2 => CommandPolarity.Off,
            _ => CommandPolarity.Neutral,
        };
}

/// <summary>
/// Rien → visible, quelque chose → replié.
///
/// Sert aux invites du genre « choisissez une étape » : elles n'ont de sens que tant que rien
/// n'est choisi. Un convertisseur nommé plutôt qu'un booléen détourné — le second marchait, mais
/// personne n'aurait deviné pourquoi.
/// </summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Conversion à sens unique.");
}
