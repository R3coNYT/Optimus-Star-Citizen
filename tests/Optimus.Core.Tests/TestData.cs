namespace Optimus.Core.Tests;

/// <summary>
/// Accès aux données réelles du dépôt depuis les tests.
///
/// Les tests s'exécutent contre <c>data/</c> plutôt que contre des jeux d'essai inventés : un
/// catalogue qui ne se charge plus, ou une action que Star Citizen a retirée, doivent faire
/// échouer la CI. C'est le seul moyen de détecter une désynchronisation avant l'utilisateur.
/// </summary>
internal static class TestData
{
    private static readonly Lazy<string> Root = new(Locate);

    public static string RepositoryRoot => Root.Value;

    private static string Locate()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "data", "commands")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Racine du dépôt introuvable depuis {AppContext.BaseDirectory} (dossier « data/commands » attendu).");
    }
}
