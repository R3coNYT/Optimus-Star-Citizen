namespace Optimus.Core.Abstractions;

/// <summary>État du jeu observé à un instant donné.</summary>
/// <param name="IsRunning">Le processus est présent.</param>
/// <param name="IsForeground">Le jeu a le focus clavier.</param>
/// <param name="ProcessId">Identifiant du processus, quand il est lisible.</param>
/// <param name="ExecutablePath">Chemin de l'exécutable — c'est de là qu'on déduit l'installation.</param>
/// <param name="Channel">Canal détecté : <c>LIVE</c>, <c>PTU</c>, <c>EPTU</c>…</param>
/// <param name="IsElevated">
/// Le jeu tourne en administrateur. Si Optimus ne l'est pas, Windows bloquera l'injection :
/// il faut le dire à l'utilisateur plutôt que de le laisser devant une commande sans effet.
/// </param>
public sealed record GameStatus(
    bool IsRunning,
    bool IsForeground,
    int? ProcessId = null,
    string? ExecutablePath = null,
    string? Channel = null,
    bool? IsElevated = null)
{
    /// <summary>Jeu absent.</summary>
    public static GameStatus NotRunning { get; } = new(false, false);

    public override string ToString() => IsRunning
        ? $"détecté (pid {ProcessId}, canal {Channel ?? "inconnu"}), premier plan : {(IsForeground ? "oui" : "non")}"
        : "non détecté";
}

/// <summary>
/// Observation du jeu.
///
/// Aucune implémentation ne doit dépendre d'un chemin d'installation en dur : on part toujours
/// du processus, et l'on demande à l'utilisateur en dernier recours.
/// </summary>
public interface IGameDetector
{
    /// <summary>Nom du jeu surveillé, pour les messages.</summary>
    string GameName { get; }

    /// <summary>Observe l'état courant. Doit être bon marché : appelé avant chaque exécution.</summary>
    GameStatus Detect();
}

/// <summary>Détecteur qui affirme que le jeu est absent. Utile aux tests et au mode simulation.</summary>
public sealed class NullGameDetector : IGameDetector
{
    public static NullGameDetector Instance { get; } = new();

    public string GameName => "aucun";

    public GameStatus Detect() => GameStatus.NotRunning;
}
