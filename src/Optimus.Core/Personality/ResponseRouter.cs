using Optimus.Core.Domain.Personality;
using Optimus.Core.Execution;

namespace Optimus.Core.Personality;

/// <summary>Ce qu'il convient de dire après une exécution.</summary>
/// <param name="Keys">
/// Clés de réplique, de la plus spécifique à la plus générale. « Portes ouvertes, commandant »
/// si la commande a sa propre entrée, « Reçu » sinon.
/// </param>
/// <param name="Event">Circonstance.</param>
/// <param name="Variables">Variables à interpoler.</param>
public sealed record ResponseRequest(
    IReadOnlyList<string> Keys,
    ResponseEvent Event,
    IReadOnlyDictionary<string, string> Variables);

/// <summary>
/// Traduit une issue d'exécution en demande de réplique.
///
/// Cette table est l'application de la règle « jamais d'échec silencieux » (RF-ERR) : chaque
/// chemin de sortie du moteur a sa réponse, y compris ceux qu'on préférerait ne jamais
/// emprunter. Un seul cas reste volontairement muet — la temporisation, où répondre reviendrait
/// à commenter un appui trop rapide.
/// </summary>
public static class ResponseRouter
{
    public static ResponseRequest? Route(ExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

        if (result.Command is not null)
        {
            variables["command"] = result.Command.Name;
        }

        if (result.Guard?.ActionId is string actionId)
        {
            variables["action"] = actionId;
        }

        return result.Status switch
        {
            ExecutionStatus.Executed or ExecutionStatus.Simulated =>
                new ResponseRequest(Keys(result, "system.success"), ResponseEvent.Success, variables),

            ExecutionStatus.Answered =>
                new ResponseRequest(Keys(result, "system.success"), ResponseEvent.Any, variables),

            ExecutionStatus.Unknown =>
                new ResponseRequest(["system.unknown_command"], ResponseEvent.Unknown, variables),

            ExecutionStatus.NeedsClarification =>
                new ResponseRequest(["system.clarify"], ResponseEvent.Clarify, variables),

            ExecutionStatus.Failed =>
                new ResponseRequest(["system.failed"], ResponseEvent.Fail, variables),

            ExecutionStatus.Rejected => RouteRejection(result, variables),

            _ => null,
        };
    }

    private static ResponseRequest? RouteRejection(
        ExecutionResult result, Dictionary<string, string> variables) => result.Guard?.Verdict switch
        {
            GuardVerdict.BindingNotConfigured =>
                new ResponseRequest(["system.no_binding"], ResponseEvent.Fail, variables),

            GuardVerdict.GameNotRunning =>
                new ResponseRequest(["system.game_not_running"], ResponseEvent.Fail, variables),

            GuardVerdict.GameNotForeground =>
                new ResponseRequest(["system.game_not_foreground"], ResponseEvent.Fail, variables),

            GuardVerdict.KillSwitch =>
                new ResponseRequest(["system.kill_switch"], ResponseEvent.Fail, variables),

            GuardVerdict.NeedsConfirmation =>
                new ResponseRequest(["system.needs_confirmation"], ResponseEvent.Clarify, variables),

            // Silence assume : l'utilisateur a simplement appuye trop vite, le lui dire serait
            // plus agacant qu'utile.
            GuardVerdict.CooldownActive => null,

            _ => new ResponseRequest(["system.failed"], ResponseEvent.Fail, variables),
        };

    private static string[] Keys(ExecutionResult result, string fallback) =>
        result.Command is null ? [fallback] : [result.Command.Id, fallback];
}
