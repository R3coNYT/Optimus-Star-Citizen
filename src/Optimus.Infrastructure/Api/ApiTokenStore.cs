using System.Text.Json;
using System.Text.Json.Serialization;
using Optimus.Core.Api;
using Optimus.Core.Diagnostics;

namespace Optimus.Infrastructure.Api;

/// <summary>
/// Les jetons d'accès du pilote, sur disque.
///
/// <b>Chiffrés par DPAPI, et pour une raison très concrète</b> : le pilote copie
/// <c>%APPDATA%\Optimus</c> d'une machine à l'autre — c'est ainsi que voyagent ses touches, ses
/// macros et ses voix. Un jeton en clair partirait avec, sur une clé USB qui traîne ou dans une
/// sauvegarde. Chiffré au compte Windows courant, le fichier copié ailleurs ne vaut rien.
///
/// Il vit dans <c>Optimus.Infrastructure</c> et non dans le cœur : DPAPI est propre à Windows,
/// et <c>Optimus.Core</c> reste délibérément neutre, donc éprouvable partout.
///
/// Ce n'est pas un coffre-fort : un programme lancé <i>par le pilote lui-même</i> peut déchiffrer
/// ce que le pilote peut déchiffrer. Mais ce programme-là a déjà accès au clavier — le jeton ne
/// lui apprendrait rien. Ce dont on se protège, c'est du fichier qui s'éloigne de sa machine.
/// </summary>
public static class ApiTokenStore
{
    /// <summary>Nom du jeton créé d'office, celui du pilote.</summary>
    public const string OwnerName = "Optimus";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Emplacement du fichier de jetons.</summary>
    public static string DefaultPath(string? root = null) =>
        Path.Combine(
            root ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Optimus"),
            "api",
            "tokens.dat");

    /// <summary>
    /// Lit les jetons, ou rend une liste vide.
    ///
    /// Un fichier illisible — déchiffrement impossible parce qu'il vient d'une autre machine,
    /// contenu corrompu — n'est pas une panne : on repart de zéro, et le pilote reçoit un jeton
    /// neuf. Refuser de démarrer pour cela serait disproportionné.
    /// </summary>
    public static IReadOnlyList<ApiToken> Load(string? path = null)
    {
        string file = path ?? DefaultPath();

        if (!File.Exists(file))
        {
            return Array.Empty<ApiToken>();
        }

        try
        {
            byte[] plain = Unprotect(File.ReadAllBytes(file));

            return JsonSerializer.Deserialize<List<ApiToken>>(plain, Json)
                   ?? (IReadOnlyList<ApiToken>)Array.Empty<ApiToken>();
        }
        catch (Exception exception)
        {
            DiagnosticLog.Warn(
                "API tokens unreadable, a fresh one will be issued",
                $"{Path.GetFileName(file)} : {exception.Message}");

            return Array.Empty<ApiToken>();
        }
    }

    /// <summary>Écrit les jetons, chiffrés.</summary>
    public static void Save(IEnumerable<ApiToken> tokens, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        string file = path ?? DefaultPath();
        string? directory = Path.GetDirectoryName(file);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(tokens.ToList(), Json);

        File.WriteAllBytes(file, Protect(plain));
    }

    /// <summary>
    /// Rend les jetons, en émettant celui du pilote s'il n'y en a aucun.
    ///
    /// L'émission d'office est ce qui rend l'API utilisable sans cérémonie : le pilote active la
    /// case, le jeton est là, il le copie. Lui demander de le créer d'abord n'aurait ajouté
    /// qu'une étape à franchir sans rien décider.
    /// </summary>
    public static IReadOnlyList<ApiToken> Ensure(string? path = null)
    {
        IReadOnlyList<ApiToken> tokens = Load(path);

        if (tokens.Count > 0)
        {
            return tokens;
        }

        ApiToken owner = ApiToken.Issue(OwnerName, ApiScope.All);
        Save([owner], path);

        DiagnosticLog.Info("API token issued", $"“{OwnerName}”, full scope");

        return [owner];
    }

    /// <summary>Remplace le secret d'un jeton. L'ancien cesse aussitôt de valoir.</summary>
    public static ApiToken Regenerate(string name, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        List<ApiToken> tokens = [.. Load(path)];
        ApiToken? existing = tokens.FirstOrDefault(
            t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        ApiToken issued = ApiToken.Issue(name, existing?.Scopes ?? ApiScope.All);

        if (existing is not null)
        {
            tokens.Remove(existing);
        }

        tokens.Add(issued);
        Save(tokens, path);

        DiagnosticLog.Info($"API token “{name}” regenerated", "the old one is now worthless");

        return issued;
    }

    // ------------------------------------------------------------------------------ DPAPI

    /// <summary>
    /// Entropie supplémentaire, pour qu'un fichier chiffré par une autre application ne se
    /// déchiffre pas par erreur avec ce code — et réciproquement.
    /// </summary>
    private static readonly byte[] Entropy = "Optimus.Api.Tokens.v1"u8.ToArray();

    private static byte[] Protect(byte[] plain) =>
        OperatingSystem.IsWindows()
            ? System.Security.Cryptography.ProtectedData.Protect(
                plain, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser)
            : plain;

    private static byte[] Unprotect(byte[] cipher) =>
        OperatingSystem.IsWindows()
            ? System.Security.Cryptography.ProtectedData.Unprotect(
                cipher, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser)
            : cipher;
}
