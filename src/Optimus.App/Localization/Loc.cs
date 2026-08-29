using System.Windows;
using Optimus.Core.Diagnostics;

namespace Optimus.App.Localization;

/// <summary>
/// Les mots de l'interface, dans la langue choisie.
///
/// Le magasin est un <see cref="ResourceDictionary"/> fusionné dans l'application, et non un
/// <c>ResourceManager</c>. La raison tient en un geste : WPF sait <b>échanger</b> un
/// dictionnaire à chaud, et tout ce qui le référence en <c>DynamicResource</c> se remet à jour
/// seul. Un <c>ResourceManager</c> aurait imposé de reconstruire la fenêtre — donc de perdre
/// l'onglet ouvert, la position du défilement et la sélection en cours — chaque fois que le
/// pilote change de langue. Y compris quand il vient de se tromper de langue et cherche à
/// revenir, ce qui est précisément le moment où il ne faut rien lui compliquer.
///
/// Les vues, elles, n'appellent rien d'ici : elles écrivent <c>{DynamicResource Clé}</c>. Cette
/// classe ne sert qu'au code, là où une phrase se compose au lieu de s'afficher.
/// </summary>
public static class Localizer
{
    /// <summary>Langue actuellement montée. Sert à ne pas refaire le travail pour rien.</summary>
    public static string Current { get; private set; } = Core.Localization.Language.Fallback;

    /// <summary>
    /// Monte les mots d'une langue, en remplaçant ceux qui étaient là.
    ///
    /// Le dictionnaire de repli reste dessous : une clé oubliée dans la traduction rend alors
    /// le français, ce qui se lit — plutôt que le nom de la clé, qui ne se lit pas.
    /// </summary>
    public static void Apply(string? language)
    {
        if (Application.Current is not Application app)
        {
            return;
        }

        string wanted = Core.Localization.Language.Resolve(language);
        string fallback = Core.Localization.Language.Fallback;

        Collection merged = new(app.Resources.MergedDictionaries);

        merged.RemoveAll(IsStrings);
        merged.Add(Load(fallback));

        if (!string.Equals(wanted, fallback, StringComparison.OrdinalIgnoreCase))
        {
            merged.Add(Load(wanted));
        }

        Current = wanted;
    }

    /// <summary>La phrase de cette clé, ou la clé elle-même si personne ne l'a traduite.</summary>
    public static string T(string key)
    {
        if (Application.Current?.TryFindResource(key) is string text)
        {
            return text;
        }

        // Rendre la clé plutôt que lever : une phrase manquante doit se voir à l'écran et se
        // corriger, jamais faire tomber l'application au milieu d'un journal.
        DiagnosticLog.Warn("texte introuvable", key);
        return key;
    }

    /// <summary>La phrase de cette clé, avec ses trous remplis.</summary>
    public static string T(string key, params object?[] arguments) =>
        string.Format(T(key), arguments);

    private static ResourceDictionary Load(string language) =>
        new()
        {
            Source = new Uri(
                $"pack://application:,,,/Localization/Strings.{Core.Localization.Language.Short(language)}.xaml",
                UriKind.Absolute),
        };

    private static bool IsStrings(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString.Contains("/Strings.", StringComparison.OrdinalIgnoreCase)
        ?? false;

    /// <summary>Petite façade pour manipuler la collection fusionnée sans la répéter.</summary>
    private readonly struct Collection(System.Collections.ObjectModel.Collection<ResourceDictionary> inner)
    {
        public void RemoveAll(Func<ResourceDictionary, bool> predicate)
        {
            foreach (ResourceDictionary found in inner.Where(predicate).ToList())
            {
                inner.Remove(found);
            }
        }

        public void Add(ResourceDictionary dictionary) => inner.Add(dictionary);
    }
}
