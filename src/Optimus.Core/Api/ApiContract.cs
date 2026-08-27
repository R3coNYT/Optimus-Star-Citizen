namespace Optimus.Core.Api;

/// <summary>
/// Ce qu'un jeton autorise.
///
/// Trois portées plutôt qu'une, parce qu'elles ne se valent pas : lire l'état d'Optimus est
/// anodin, changer un réglage l'est moins, et <b>appuyer sur des touches dans un jeu en cours
/// est la seule qui puisse coûter un vaisseau</b>. Un bot Discord qui affiche l'état n'a besoin
/// que de <see cref="Read"/> ; lui donner <see cref="Execute"/> par commodité reviendrait à
/// confier le clavier à un service qui n'en a aucun usage.
/// </summary>
[Flags]
public enum ApiScope
{
    None = 0,

    /// <summary>Consulter : état, catalogue, résolution d'un énoncé sans l'exécuter.</summary>
    Read = 1,

    /// <summary>Modifier : simulation, arrêt d'urgence, faire parler le copilote.</summary>
    Write = 2,

    /// <summary>Exécuter une commande, donc envoyer des touches au jeu.</summary>
    Execute = 4,

    /// <summary>Tout, pour le jeton du pilote lui-même.</summary>
    All = Read | Write | Execute,
}

/// <summary>
/// Réglages de l'API locale.
/// </summary>
/// <param name="Enabled">
/// Faux par défaut. Une interface qu'on n'a pas demandée est une surface d'attaque offerte,
/// même sur la boucle locale — n'importe quel programme lancé par le pilote pourrait s'y
/// adresser.
/// </param>
/// <param name="Port">
/// Port d'écoute sur <c>127.0.0.1</c>. 8731 par défaut, choisi hors des plages courantes pour
/// ne pas se disputer le port d'un serveur de développement.
/// </param>
/// <param name="ExecutionsPerMinute">
/// Plafond d'exécutions par minute et par jeton. Un client qui s'emballe ne doit pas pouvoir
/// marteler le clavier du pilote : ce n'est pas un souci de charge — la machine tiendrait — mais
/// de vaisseau, où trente commandes en rafale sont ingérables.
/// </param>
public sealed record ApiSettings(
    bool Enabled = false,
    int Port = 8731,
    int ExecutionsPerMinute = 30)
{
    public static ApiSettings Disabled { get; } = new();

    /// <summary>Adresse d'écoute. Toujours la boucle locale, jamais une interface réseau.</summary>
    public string Prefix => $"http://127.0.0.1:{Port}/";
}

/// <summary>Un jeton d'accès et ce qu'il permet.</summary>
/// <param name="Name">Nom lisible du client : « Optimus », « Discord », « Stream Deck ».</param>
/// <param name="Secret">Secret porté par l'en-tête <c>Authorization: Bearer …</c>.</param>
/// <param name="Scopes">Ce que ce client a le droit de faire.</param>
/// <param name="CreatedAt">Date d'émission, pour qu'un jeton oublié se reconnaisse.</param>
public sealed record ApiToken(
    string Name,
    string Secret,
    ApiScope Scopes,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Longueur du secret, en octets.
    ///
    /// 32 octets, soit 256 bits. Généreux pour une interface qui n'écoute que la boucle locale,
    /// mais un secret court n'aurait rien économisé — il se copie une fois et se colle une fois.
    /// </summary>
    public const int SecretBytes = 32;

    /// <summary>Émet un jeton neuf, au hasard cryptographique.</summary>
    public static ApiToken Issue(string name, ApiScope scopes, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        byte[] secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(SecretBytes);

        return new ApiToken(
            name,
            Convert.ToBase64String(secret).Replace('+', '-').Replace('/', '_').TrimEnd('='),
            scopes,
            (time ?? TimeProvider.System).GetUtcNow());
    }

    /// <summary>
    /// Comparaison à temps constant.
    ///
    /// Une comparaison ordinaire s'arrête au premier octet différent, et sa durée trahit donc le
    /// nombre de caractères devinés. Sur la boucle locale l'attaque est difficile ; elle n'est
    /// pas impossible, et la parade tient en trois lignes.
    /// </summary>
    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(Secret),
            System.Text.Encoding.UTF8.GetBytes(candidate));
    }
}
