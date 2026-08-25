namespace Optimus.Core.Domain.Personality;

/// <summary>Circonstance qui déclenche une règle de comportement.</summary>
public enum BehaviorTrigger
{
    /// <summary>Le pilote est en combat.</summary>
    CombatActive,

    /// <summary>La dernière commande a échoué ou a été refusée.</summary>
    CommandFailed,

    /// <summary>La même commande échoue de façon répétée.</summary>
    RepeatedFailure,

    /// <summary>L'énoncé n'a pas été compris.</summary>
    CommandUnknown,

    /// <summary>Aucune interaction depuis un moment.</summary>
    IdleLong,

    /// <summary>Optimus vient de démarrer.</summary>
    Startup,
}

/// <summary>Façon dont le copilote adapte sa réponse.</summary>
public enum BehaviorEffect
{
    /// <summary>Abréger. En combat, un copilote bavard est insupportable.</summary>
    ShortResponses,

    /// <summary>Donner la cause plutôt qu'un constat d'échec.</summary>
    ExplainReason,

    /// <summary>Proposer une correction : c'est la troisième fois que ça échoue.</summary>
    SuggestFix,

    /// <summary>Rester factuel, sans humour ni ironie.</summary>
    StayNeutral,

    /// <summary>Prendre la parole spontanément.</summary>
    Speak,
}

/// <summary>
/// Règle de comportement : dans telle circonstance, adapter la réponse de telle façon.
///
/// C'est ce qui distingue un copilote d'un lecteur de fichier de répliques. La même commande
/// échouée produit « Négatif » en vol tranquille, mais « Négatif — aucun raccourci pour cette
/// action » après le troisième essai, et rien du tout en plein combat si l'on a demandé le
/// silence.
/// </summary>
/// <param name="Trigger">Circonstance.</param>
/// <param name="Effect">Adaptation.</param>
/// <param name="Priority">Les priorités élevées l'emportent en cas de conflit.</param>
/// <param name="Threshold">Seuil, pour les règles qui comptent — nombre d'échecs, minutes d'inactivité.</param>
/// <param name="MaxWords">Budget de mots imposé, s'il y en a un.</param>
/// <param name="ResponseKey">Clé de réplique spécifique à employer.</param>
public sealed record BehaviorRule(
    BehaviorTrigger Trigger,
    BehaviorEffect Effect,
    int Priority = 50,
    int Threshold = 0,
    int? MaxWords = null,
    string? ResponseKey = null);

/// <summary>
/// Ce que le copilote sait de la situation au moment de répondre.
///
/// Volontairement modeste : tout ceci est <b>déclaratif</b>, déduit de ce que le pilote a dit
/// et de ce qui vient de se passer, faute de télémétrie. Le jour où le jeu exposera son état,
/// un <c>IGameStateProvider</c> remplira les mêmes champs avec de vraies données, sans que le
/// moteur de personnalité ait à changer.
/// </summary>
/// <param name="CombatActive">Le pilote a annoncé passer en combat.</param>
/// <param name="ConsecutiveFailures">Échecs consécutifs de la même commande.</param>
/// <param name="LastCommandId">Dernière commande traitée.</param>
/// <param name="SinceLastInteraction">Temps écoulé depuis le dernier échange.</param>
public readonly record struct CopilotContext(
    bool CombatActive = false,
    int ConsecutiveFailures = 0,
    string? LastCommandId = null,
    TimeSpan SinceLastInteraction = default);

/// <summary>Adaptation retenue après arbitrage des règles.</summary>
/// <param name="MaxWords">Budget de mots, s'il est imposé.</param>
/// <param name="AllowHumor">L'humour et l'ironie sont-ils de mise.</param>
/// <param name="ExplainReason">Faut-il donner la cause de l'échec.</param>
/// <param name="PreferredKeys">Clés de réplique à essayer en priorité.</param>
/// <param name="AppliedRules">Règles ayant joué, pour le mode debug.</param>
public sealed record EffectiveBehavior(
    int? MaxWords,
    bool AllowHumor,
    bool ExplainReason,
    IReadOnlyList<string> PreferredKeys,
    IReadOnlyList<BehaviorTrigger> AppliedRules)
{
    public static EffectiveBehavior Neutral { get; } =
        new(null, true, false, Array.Empty<string>(), Array.Empty<BehaviorTrigger>());
}

/// <summary>
/// Arbitre les règles de comportement.
///
/// Les règles applicables sont triées par priorité et leurs effets fusionnés ; la plus
/// prioritaire l'emporte sur les valeurs en conflit. L'ensemble retenu est exposé dans
/// <see cref="EffectiveBehavior.AppliedRules"/> — sans quoi le comportement du copilote
/// deviendrait inexplicable pour l'utilisateur, ce qui est le pire défaut d'un système de
/// règles (docs/08).
/// </summary>
public static class BehaviorEngine
{
    /// <summary>Budget de mots accordé à une explication, quand aucune brièveté n'est imposée.</summary>
    private const int ExplanationWordBudget = 24;

    public static EffectiveBehavior Resolve(
        IReadOnlyList<BehaviorRule> rules,
        CopilotContext context,
        ResponseEvent responseEvent)
    {
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Count == 0)
        {
            return EffectiveBehavior.Neutral;
        }

        List<BehaviorRule> applicable = rules
            .Where(rule => Applies(rule, context, responseEvent))
            .OrderByDescending(rule => rule.Priority)
            .ToList();

        if (applicable.Count == 0)
        {
            return EffectiveBehavior.Neutral;
        }

        int? maxWords = null;
        bool allowHumor = true;
        bool explainReason = false;
        List<string> keys = new();
        List<BehaviorTrigger> applied = new();

        // Parcours du moins prioritaire au plus prioritaire : la derniere ecriture gagne.
        for (int i = applicable.Count - 1; i >= 0; i--)
        {
            BehaviorRule rule = applicable[i];
            applied.Add(rule.Trigger);

            switch (rule.Effect)
            {
                case BehaviorEffect.ShortResponses:
                    maxWords = rule.MaxWords ?? 8;
                    allowHumor = false;
                    break;

                case BehaviorEffect.StayNeutral:
                    allowHumor = false;
                    break;

                case BehaviorEffect.ExplainReason:
                    explainReason = true;
                    break;

                case BehaviorEffect.SuggestFix:
                    explainReason = true;
                    if (rule.ResponseKey is not null)
                    {
                        keys.Insert(0, rule.ResponseKey);
                    }

                    break;

                case BehaviorEffect.Speak:
                default:
                    if (rule.ResponseKey is not null)
                    {
                        keys.Insert(0, rule.ResponseKey);
                    }

                    break;
            }
        }

        applied.Reverse();

        // Expliquer demande des mots. Sans ce desserrement, la verbosite tronquait « Troisieme
        // echec. Le raccourci de X n'est pas configure. » a ses trois premiers mots - amputant
        // la reponse de la seule partie qui servait a quelque chose. Une regle qui s'annule
        // elle-meme est pire qu'une regle absente.
        //
        // La brievete imposee (combat) reste prioritaire : si un budget a ete fixe
        // explicitement, on ne le contredit pas.
        if (explainReason && maxWords is null)
        {
            maxWords = ExplanationWordBudget;
        }

        return new EffectiveBehavior(maxWords, allowHumor, explainReason, keys, applied);
    }

    private static bool Applies(BehaviorRule rule, CopilotContext context, ResponseEvent responseEvent) =>
        rule.Trigger switch
        {
            BehaviorTrigger.CombatActive => context.CombatActive,
            BehaviorTrigger.CommandFailed => responseEvent == ResponseEvent.Fail,
            BehaviorTrigger.RepeatedFailure =>
                responseEvent == ResponseEvent.Fail &&
                context.ConsecutiveFailures >= Math.Max(2, rule.Threshold),
            BehaviorTrigger.CommandUnknown => responseEvent == ResponseEvent.Unknown,
            BehaviorTrigger.IdleLong =>
                context.SinceLastInteraction > TimeSpan.FromMinutes(Math.Max(1, rule.Threshold)),
            BehaviorTrigger.Startup => false, // declenchee explicitement, pas par une reponse
            _ => false,
        };
}
