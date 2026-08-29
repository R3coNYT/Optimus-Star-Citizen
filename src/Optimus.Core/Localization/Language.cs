namespace Optimus.Core.Localization;

/// <summary>
/// Les langues qu'Optimus parle.
///
/// Une seule valeur commande tout : l'écran, la grammaire de reconnaissance, la langue passée
/// à Whisper, les répliques du copilote et le choix de la voix. C'est la même leçon que le
/// changement de copilote (D70) — mot d'éveil, voix et caractère changent ensemble, ou le
/// pilote se retrouve avec un assemblage que personne n'a voulu.
///
/// Le français reste le <b>repli</b>, et non par préférence : c'est la langue dans laquelle le
/// catalogue a été écrit et éprouvé. Un fichier anglais absent ou abîmé doit ramener Optimus à
/// quelque chose qui fonctionne, jamais à un écran muet.
/// </summary>
public static class Language
{
    public const string French = "fr-FR";
    public const string English = "en-US";

    /// <summary>Ce vers quoi on retombe quand une langue est inconnue ou son fichier absent.</summary>
    public const string Fallback = French;

    /// <summary>Les langues proposées au pilote, dans l'ordre où l'écran les montre.</summary>
    public static IReadOnlyList<string> Known { get; } = [French, English];

    /// <summary>Le nom de la langue, écrit dans cette langue — c'est ainsi qu'on choisit.</summary>
    public static string DisplayName(string? language) =>
        Resolve(language) == English ? "English" : "Français";

    /// <summary>Le code à deux lettres : « fr », « en ». C'est lui qui suffixe les fichiers.</summary>
    public static string Short(string? language) =>
        Resolve(language)[..2];

    /// <summary>
    /// Ramène une valeur quelconque à une langue connue.
    ///
    /// Tolérant par choix : « en », « en-GB » et « EN-US » désignent tous l'anglais. Un profil
    /// écrit à la main ne doit pas priver son auteur de sa langue pour une graphie.
    /// </summary>
    public static string Resolve(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return Fallback;
        }

        return language.Trim()[..Math.Min(2, language.Trim().Length)].ToLowerInvariant() switch
        {
            "en" => English,
            "fr" => French,
            _ => Fallback,
        };
    }

    /// <summary>
    /// Le fichier de cette langue s'il existe, celui du repli sinon, et enfin le fichier sans
    /// suffixe.
    ///
    /// Le fichier <b>sans suffixe</b> est celui d'origine, écrit en français avant que la
    /// question des langues se pose. Le renommer aurait touché neuf fichiers de test et deux
    /// scripts pour un gain d'esthétique ; il tient donc le rôle de dernier recours, ce qu'un
    /// commentaire suffit à dire.
    /// </summary>
    /// <param name="directory">Dossier où chercher.</param>
    /// <param name="stem">Nom du fichier sans langue ni extension, par exemple « responses ».</param>
    /// <param name="extension">Extension, point compris.</param>
    /// <param name="language">Langue demandée.</param>
    /// <returns>Le chemin retenu, ou <c>null</c> si rien n'existe.</returns>
    public static string? Localized(string directory, string stem, string extension, string? language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);

        foreach (string candidate in Candidates(directory, stem, extension, language))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(
        string directory, string stem, string extension, string? language)
    {
        yield return Path.Combine(directory, $"{stem}.{Short(language)}{extension}");
        yield return Path.Combine(directory, $"{stem}.{Short(Fallback)}{extension}");
        yield return Path.Combine(directory, $"{stem}{extension}");
    }
}
