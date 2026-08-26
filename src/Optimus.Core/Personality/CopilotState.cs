using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Execution;

namespace Optimus.Core.Personality;

/// <summary>
/// Mémoire de session du copilote.
///
/// Volontairement minuscule et <b>déclarative</b> : Optimus ne sait du combat que ce que le
/// pilote lui en a dit, et des échecs que ce qu'il vient d'observer. C'est l'implémentation
/// modeste du <c>GameContext</c> de docs/04, en attendant qu'une télémétrie existe. Le jour où
/// elle existera, elle remplira les mêmes champs sans que le moteur de personnalité change.
/// </summary>
public sealed class CopilotState
{
    private readonly TimeProvider _time;
    private DateTimeOffset _lastInteraction;
    private string? _lastFailedCommandId;

    public CopilotState(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _lastInteraction = _time.GetUtcNow();
    }

    /// <summary>Le pilote a annoncé être en configuration de combat.</summary>
    public bool CombatActive { get; private set; }

    /// <summary>Échecs consécutifs de la même commande.</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>Dernière commande traitée, quelle qu'en soit l'issue.</summary>
    public string? LastCommandId { get; private set; }

    /// <summary>Bascule le mode de combat. Appelé quand le pilote demande le changement de mode.</summary>
    public bool ToggleCombat()
    {
        CombatActive = !CombatActive;
        return CombatActive;
    }

    /// <summary>Force le mode de combat, quand on le connaît vraiment.</summary>
    public void SetCombat(bool active) => CombatActive = active;

    /// <summary>
    /// Aligne le mode déclaré sur ce que le pilote vient de demander. Retourne l'état atteint.
    /// </summary>
    /// <param name="polarity">
    /// Sens porté par l'intention, quand la formulation le dit. Depuis que le jeu expose
    /// <c>v_master_mode_set_scm</c> et <c>v_master_mode_set_nav</c>, « mode combat » et « mode
    /// navigation » sont des phrases <b>polarisées</b> : la réponse est déjà là, et la relire
    /// dans les mots reviendrait à la déduire deux fois — deux occasions de diverger.
    /// </param>
    /// <param name="normalizedUtterance">
    /// Repli pour les formulations neutres — « change de mode » — où seule une bascule a du sens.
    /// </param>
    public bool ApplyMasterMode(
        CommandPolarity polarity = CommandPolarity.Neutral,
        string? normalizedUtterance = null)
    {
        bool? target = polarity switch
        {
            CommandPolarity.On => true,
            CommandPolarity.Off => false,
            _ => MasterMode.Intended(normalizedUtterance),
        };

        if (target is bool wanted)
        {
            SetCombat(wanted);
        }
        else
        {
            ToggleCombat();
        }

        return CombatActive;
    }

    /// <summary>
    /// Enregistre l'issue d'une exécution.
    ///
    /// Le compteur d'échecs ne se cumule que pour <b>la même</b> commande : trois refus
    /// successifs sur trois commandes différentes ne disent rien, trois refus sur la même
    /// disent qu'il manque un raccourci.
    /// </summary>
    public void Record(ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _lastInteraction = _time.GetUtcNow();
        LastCommandId = result.Command?.Id ?? LastCommandId;

        bool failed = result.Status is ExecutionStatus.Rejected or ExecutionStatus.Failed;

        if (!failed)
        {
            ConsecutiveFailures = 0;
            _lastFailedCommandId = null;
            return;
        }

        string? commandId = result.Command?.Id;

        if (commandId is not null && commandId == _lastFailedCommandId)
        {
            ConsecutiveFailures++;
        }
        else
        {
            ConsecutiveFailures = 1;
            _lastFailedCommandId = commandId;
        }
    }

    /// <summary>Note une interaction sans exécution : une question, un dialogue.</summary>
    public void Touch() => _lastInteraction = _time.GetUtcNow();

    /// <summary>Instantané du contexte, tel que les règles le consomment.</summary>
    public CopilotContext Snapshot() => new(
        CombatActive,
        ConsecutiveFailures,
        LastCommandId,
        _time.GetUtcNow() - _lastInteraction);
}
