using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Execution;

/// <summary>État du système au moment d'une exécution.</summary>
/// <param name="KillSwitchEngaged">Arrêt d'urgence activé : plus rien ne sort.</param>
/// <param name="SimulationMode">Mode simulation : on trace, on n'appuie pas.</param>
/// <param name="GameRunning">Le jeu est-il lancé.</param>
/// <param name="GameForeground">Le jeu a-t-il le focus.</param>
/// <param name="RequireGameForeground">Exiger le focus du jeu avant d'envoyer une entrée.</param>
/// <param name="ConfirmDangerous">Exiger une confirmation pour les commandes marquées dangereuses.</param>
public sealed record ExecutionEnvironment(
    bool KillSwitchEngaged = false,
    bool SimulationMode = false,
    bool GameRunning = true,
    bool GameForeground = true,
    bool RequireGameForeground = true,
    bool ConfirmDangerous = true,
    bool CombatActive = false)
{
    /// <summary>Environnement des tests et du mode simulation : rien n'est exigé du monde extérieur.</summary>
    public static ExecutionEnvironment Sandbox { get; } = new(
        GameRunning: false,
        GameForeground: false,
        RequireGameForeground: false,
        SimulationMode: true);
}

/// <summary>Motif de refus, ou autorisation.</summary>
public enum GuardVerdict
{
    Allowed,
    KillSwitch,
    GameNotRunning,
    GameNotForeground,
    CooldownActive,
    NeedsConfirmation,
    BindingNotConfigured,
    ActionUnknown,
    ActionUnsupported,
}

/// <summary>Décision du garde, avec de quoi construire une réponse utile.</summary>
/// <param name="Verdict">Issue.</param>
/// <param name="Detail">Précision destinée à l'utilisateur ou au journal.</param>
/// <param name="ActionId">Action fautive, quand le refus vient d'un binding.</param>
public sealed record GuardDecision(GuardVerdict Verdict, string? Detail = null, string? ActionId = null)
{
    public bool IsAllowed => Verdict == GuardVerdict.Allowed;

    public static GuardDecision Allow { get; } = new(GuardVerdict.Allowed);
}

/// <summary>
/// Point de contrôle unique avant toute exécution.
///
/// Tout ce qui peut empêcher une commande de partir est réuni ici, et nulle part ailleurs :
/// arrêt d'urgence, simulation, présence du jeu, focus, temporisation, confirmation des actions
/// dangereuses, existence du raccourci. Un seul endroit à auditer, un seul endroit à tester.
/// </summary>
public sealed class ExecutionGuard
{
    private readonly Dictionary<string, DateTimeOffset> _lastExecution = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    public ExecutionGuard(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Évalue une commande. <paramref name="confirmed"/> indique que l'utilisateur a déjà
    /// confirmé une action dangereuse.
    /// </summary>
    /// <param name="steps">
    /// Séquence réellement retenue. Une commande à polarité peut viser une séquence dirigée
    /// plutôt que sa bascule ; c'est celle-là qu'il faut exiger liée, pas l'autre.
    /// </param>
    public GuardDecision Evaluate(
        CommandDefinition command,
        BindingProfile bindings,
        ExecutionEnvironment environment,
        bool confirmed = false,
        IReadOnlyList<ActionStep>? steps = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.KillSwitchEngaged)
        {
            return new GuardDecision(GuardVerdict.KillSwitch, "Arrêt d'urgence actif.");
        }

        if (IsOnCooldown(command))
        {
            return new GuardDecision(GuardVerdict.CooldownActive, $"Temporisation de {command.CooldownMs} ms.");
        }

        if (command.Dangerous && environment.ConfirmDangerous && !confirmed)
        {
            return new GuardDecision(GuardVerdict.NeedsConfirmation, $"« {command.Name} » demande une confirmation.");
        }

        // Une commande passive ne touche pas au jeu : ni focus ni binding à exiger.
        if (command.IsPassive)
        {
            return GuardDecision.Allow;
        }

        // En simulation, on veut précisément pouvoir tester sans le jeu.
        if (!environment.SimulationMode)
        {
            if (!environment.GameRunning)
            {
                return new GuardDecision(GuardVerdict.GameNotRunning, "Star Citizen n'est pas lancé.");
            }

            if (environment.RequireGameForeground && !environment.GameForeground)
            {
                return new GuardDecision(GuardVerdict.GameNotForeground, "Star Citizen n'est pas au premier plan.");
            }
        }

        IEnumerable<string> requiredActions = steps is null
            ? command.ReferencedActionIds
            : steps.Where(a => a.Type == ActionStepType.GameAction && a.ActionId is not null)
                   .Select(a => a.ActionId!);

        foreach (string actionId in requiredActions)
        {
            BindingLookup lookup = bindings.Resolve(actionId, out _);
            switch (lookup)
            {
                case BindingLookup.NotBound:
                    // L'action est nommee dans le message : sur une macro de dix pas, savoir
                    // LEQUEL manque est toute la difference entre un diagnostic et une enigme.
                    return new GuardDecision(
                        GuardVerdict.BindingNotConfigured,
                        $"Aucun raccourci configuré pour « {actionId} ».",
                        actionId);

                case BindingLookup.UnknownAction:
                    return new GuardDecision(
                        GuardVerdict.ActionUnknown,
                        "Cette action est inconnue du profil de touches.",
                        actionId);

                case BindingLookup.Unsupported:
                    return new GuardDecision(
                        GuardVerdict.ActionUnsupported,
                        "Cette action utilise un axe analogique, qu'Optimus ne sait pas piloter.",
                        actionId);

                case BindingLookup.Bound:
                default:
                    break;
            }
        }

        return GuardDecision.Allow;
    }

    /// <summary>À appeler après une exécution réussie, pour armer la temporisation.</summary>
    public void MarkExecuted(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _lastExecution[command.Id] = _time.GetUtcNow();
    }

    /// <summary>Oublie toutes les temporisations. Utile aux tests et après un arrêt d'urgence.</summary>
    public void ResetCooldowns() => _lastExecution.Clear();

    private bool IsOnCooldown(CommandDefinition command)
    {
        if (command.CooldownMs <= 0)
        {
            return false;
        }

        if (!_lastExecution.TryGetValue(command.Id, out DateTimeOffset last))
        {
            return false;
        }

        return _time.GetUtcNow() - last < TimeSpan.FromMilliseconds(command.CooldownMs);
    }
}
