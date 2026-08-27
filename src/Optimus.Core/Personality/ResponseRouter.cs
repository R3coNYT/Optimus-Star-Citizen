using Optimus.Core.Domain.Commands;
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
    /// <param name="context">
    /// Situation courante, consultée pour les commandes dont la réplique en dépend. Le mode de
    /// vol en est le seul cas : une même commande y mène dans les deux sens, et « Armes chaudes »
    /// ne vaut que dans un sens.
    /// </param>
    public static ResponseRequest? Route(ExecutionResult result, CopilotContext context = default)
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

        // La sequence a deja parle pour elle-meme : ajouter un « Recu » par-dessus reviendrait
        // a doubler la voix du copilote.
        if (result.Narrated && result.Succeeded)
        {
            return null;
        }

        return result.Status switch
        {
            ExecutionStatus.Executed or ExecutionStatus.Simulated =>
                new ResponseRequest(Keys(result, "system.success", context), ResponseEvent.Success, variables),

            ExecutionStatus.Answered =>
                new ResponseRequest(Keys(result, "system.success", context), ResponseEvent.Any, variables),

            // Rien envoye parce que rien n'etait utile. Ce n'est pas un echec : le compter comme
            // tel declencherait « echoue systematiquement » apres trois demandes satisfaites.
            ExecutionStatus.NoChangeNeeded =>
                new ResponseRequest(["system.already_in_state"], ResponseEvent.Any, variables),

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

    /// <summary>
    /// Clé de réplique dirigée : <c>ship.lights.toggle</c> devient <c>ship.lights.on</c>. Le
    /// suffixe « toggle » ne décrit plus rien une fois le sens connu.
    /// </summary>
    private static string DirectedKey(string commandId, CommandPolarity polarity)
    {
        string suffix = polarity == CommandPolarity.On ? "on" : "off";

        return commandId.EndsWith(".toggle", StringComparison.Ordinal)
            ? string.Concat(commandId.AsSpan(0, commandId.Length - "toggle".Length), suffix)
            : $"{commandId}.{suffix}";
    }

    private static string[] Keys(ExecutionResult result, string fallback, CopilotContext context)
    {
        if (result.Command is null)
        {
            return [fallback];
        }

        // Le mode de vol se commute par une commande unique, mais s'annonce dans les deux sens :
        // « Armes chaudes » en entrant en combat, « Retour en navigation » en sortant. Sans cette
        // clé, les deux entrées écrites pour cela ne servaient jamais.
        if (result.Command.Id == MasterMode.CommandId)
        {
            return [MasterMode.ResponseKey(context.CombatActive), result.Command.Id, fallback];
        }

        // Les commandes de bascule de profil sont engendrees a partir des fichiers du pilote :
        // leur identifiant depend d'un nom qu'on ne connait pas a l'avance, et aucune reponse ne
        // peut donc etre ecrite pour lui. Une cle commune leur sert de point de chute.
        if (result.Command.Id.StartsWith(Bindings.BindingProfileSet.CommandPrefix, StringComparison.Ordinal))
        {
            return [Bindings.BindingProfileSet.ResponseKey, fallback];
        }

        // Le sens demande prime sur la commande : « Voila de la lumiere » apres une extinction
        // sonnerait faux. On tente d'abord la cle dirigee, et l'on retombe sur la cle generale
        // pour toutes les commandes ou ecrire deux jeux de repliques ne vaut pas le detour.
        if (result.Polarity != CommandPolarity.Neutral)
        {
            return [DirectedKey(result.Command.Id, result.Polarity), result.Command.Id, fallback];
        }

        return [result.Command.Id, fallback];
    }
}
