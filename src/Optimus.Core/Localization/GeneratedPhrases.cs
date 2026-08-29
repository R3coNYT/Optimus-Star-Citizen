namespace Optimus.Core.Localization;

/// <summary>
/// Les formulations qu'Optimus <b>engendre</b>, par opposition à celles qu'il lit.
///
/// Deux familles seulement, et elles ont la même raison d'être : un copilote et un profil de
/// touches portent des noms que le pilote choisit, si bien qu'aucun catalogue livré ne peut
/// contenir leurs commandes de bascule à l'avance. Elles se fabriquent donc à partir du nom.
///
/// Elles vivent ici plutôt que dans le catalogue parce qu'elles ne sont pas du contenu :
/// « passe à Virgil » n'a pas à être traduit une fois par copilote installé. Mais elles
/// entrent dans la <b>grammaire de reconnaissance</b> comme les autres, et doivent donc
/// suivre la langue — sans quoi un pilote anglophone ne pourrait changer de copilote qu'en
/// prononçant une phrase française.
/// </summary>
public static class GeneratedPhrases
{
    /// <summary>Les façons de demander un autre copilote.</summary>
    public static IReadOnlyList<string> ForCopilot(string name, string? language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string spoken = name.Trim();

        return Language.Resolve(language) == Language.English
            ?
            [
                $"switch to {spoken}",
                $"call {spoken}",
                $"copilot {spoken}",
            ]
            :
            [
                $"passe à {spoken}",
                $"appelle {spoken}",
                $"copilote {spoken}",
            ];
    }

    /// <summary>Les façons de demander un autre profil de touches.</summary>
    public static IReadOnlyList<string> ForBindingProfile(string name, string? language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string spoken = name.Trim();

        return Language.Resolve(language) == Language.English
            ?
            [
                $"{spoken} profile",
                $"switch to {spoken} profile",
                $"{spoken} keys",
            ]
            :
            [
                $"profil {spoken}",
                $"passe en profil {spoken}",
                $"touches {spoken}",
            ];
    }

    /// <summary>Le nom affiché d'une commande de bascule de profil.</summary>
    public static string BindingProfileName(string name, string? language) =>
        Language.Resolve(language) == Language.English
            ? $"{name} profile"
            : $"Profil {name}";

    /// <summary>La catégorie sous laquelle ces commandes se rangent à l'écran.</summary>
    public static string CopilotCategory(string? language) =>
        Language.Resolve(language) == Language.English ? "copilots" : "copilotes";

    /// <inheritdoc cref="CopilotCategory"/>
    public static string BindingProfileCategory(string? language) =>
        Language.Resolve(language) == Language.English ? "keys" : "touches";
}
