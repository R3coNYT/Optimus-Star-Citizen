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

/// <summary>
/// Profondeur d'imbrication → marge gauche.
///
/// L'indentation n'est pas cosmétique ici : elle est le seul indice visuel de ce qu'un bloc
/// contient. Une séquence dont les branches s'aligneraient toutes à gauche se lirait comme une
/// liste linéaire, et un pas placé dans le « sinon » passerait pour un pas placé après le « si ».
/// </summary>
public sealed class DepthToMarginConverter : IValueConverter
{
    /// <summary>Retrait par niveau. Assez pour se voir, assez peu pour tenir en profondeur.</summary>
    private const double Step = 18;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? depth * Step : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Conversion à sens unique.");
}

/// <summary>
/// Sujet de condition ↔ rang dans la liste déroulante.
///
/// L'ordre est celui de <see cref="ConditionSubject"/> : le certain d'abord, le supposé
/// ensuite. Ce n'est pas un hasard de rédaction — la liste se lit de haut en bas, et ce qui
/// vaut le plus mérite d'être proposé le premier.
/// </summary>
public sealed class SubjectToIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ConditionSubject subject ? (int)subject : 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && Enum.IsDefined(typeof(ConditionSubject), index)
            ? (ConditionSubject)index
            : ConditionSubject.Binding;
}
