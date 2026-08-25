namespace Optimus.Core.Personality;

/// <summary>
/// Le mode de vol, tel qu'Optimus peut le suivre.
///
/// Star Citizen n'expose rien : une seule touche — B maintenue — fait <b>cycler</b> entre NAV et
/// SCM. Le catalogue n'a donc qu'une commande, que l'on atteint aussi bien en disant « mode
/// combat » qu'en disant « mode navigation ».
///
/// Basculer l'état à chaque appel donnait par conséquent l'inverse de ce qui était demandé une
/// fois sur deux : le pilote disait « mode navigation » et Optimus se croyait au combat, avec
/// toutes les règles de brièveté qui s'ensuivent. L'énoncé porte l'intention — on la lit, plutôt
/// que de la deviner.
///
/// Reste une limite qu'aucune astuce ne lève : si le vaisseau n'était pas dans le mode supposé,
/// la touche le fera cycler vers le mauvais, et Optimus n'a aucun moyen de s'en apercevoir. C'est
/// le prix de l'absence de télémétrie, et ce sera le premier gain d'un <c>IGameStateProvider</c>.
/// </summary>
public static class MasterMode
{
    /// <summary>Commande du catalogue qui commute le mode de vol.</summary>
    public const string CommandId = "nav.master_mode.cycle";

    /// <summary>Réplique dédiée au passage en configuration de combat.</summary>
    public const string CombatResponseKey = "nav.master_mode.combat";

    /// <summary>Réplique dédiée au retour en navigation.</summary>
    public const string CalmResponseKey = "nav.master_mode.calm";

    private static readonly string[] CombatWords = ["combat", "scm", "arme", "armes"];
    private static readonly string[] CalmWords = ["navigation", "nav", "croisiere", "voyage"];

    /// <summary>
    /// État visé par cet énoncé : vrai pour le combat, faux pour la navigation, <c>null</c> si la
    /// phrase ne tranche pas — « change de mode » — auquel cas il ne reste qu'à basculer.
    /// </summary>
    public static bool? Intended(string? normalizedUtterance)
    {
        if (string.IsNullOrWhiteSpace(normalizedUtterance))
        {
            return null;
        }

        string[] words = normalizedUtterance.Split(
            [' ', '\t', '\'', '-'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string word in words)
        {
            if (Array.Exists(CombatWords, w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (Array.Exists(CalmWords, w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        return null;
    }

    /// <summary>Clé de réplique correspondant à l'état atteint.</summary>
    public static string ResponseKey(bool combatActive) =>
        combatActive ? CombatResponseKey : CalmResponseKey;
}
