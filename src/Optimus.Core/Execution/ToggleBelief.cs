using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Execution;

/// <summary>
/// Ce qu'Optimus <b>croit</b> savoir de l'état des bascules.
///
/// Sans cette mémoire, « éteins les lumières » n'est qu'un synonyme d'« allume les lumières » :
/// la touche est la même, elle inverse, et demander l'extinction alors que tout est déjà éteint
/// rallume. Avec elle, Optimus s'abstient et le dit.
///
/// Le mot <i>croit</i> est à prendre au pied de la lettre. Rien ne remonte du jeu (D32) : cette
/// mémoire n'enregistre que les commutations qu'Optimus a lui-même provoquées. Que le pilote
/// touche la même fonction au clavier, et elle devient fausse sans que rien ne le signale.
///
/// D'où la porte de sortie, qui est le point important : <b>redemander la même chose passe
/// outre</b>. Un pilote à qui l'on répond « c'est déjà éteint » alors que ça ne l'est pas répète
/// son ordre, et il est alors exécuté. Une croyance erronée coûte donc un aller-retour, jamais
/// un blocage — c'est ce qui rend le pari acceptable.
///
/// Ne concerne que les commandes sans action dirigée liée : lorsque <c>v_lights_off</c> a une
/// touche, il n'y a plus rien à supposer.
/// </summary>
public sealed class ToggleBelief
{
    private readonly Dictionary<string, bool> _believed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refusedOnce = new(StringComparer.Ordinal);

    /// <summary>État supposé, ou <c>null</c> tant qu'Optimus n'a rien commuté lui-même.</summary>
    public bool? Believed(string commandId) =>
        _believed.TryGetValue(commandId, out bool value) ? value : null;

    /// <summary>
    /// Vrai si la demande n'a visiblement rien à changer. Le second appel identique retourne
    /// faux : le pilote insiste, c'est qu'on se trompait.
    /// </summary>
    public bool IsRedundant(string commandId, CommandPolarity polarity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        if (polarity == CommandPolarity.Neutral)
        {
            return false;
        }

        string key = Key(commandId, polarity);

        if (_refusedOnce.Contains(key))
        {
            _refusedOnce.Remove(key);
            return false;
        }

        bool wanted = polarity == CommandPolarity.On;

        if (Believed(commandId) != wanted)
        {
            return false;
        }

        _refusedOnce.Add(key);
        return true;
    }

    /// <summary>Enregistre une commutation effectivement envoyée au jeu.</summary>
    public void RecordApplied(string commandId, CommandPolarity polarity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        _refusedOnce.Remove(Key(commandId, CommandPolarity.On));
        _refusedOnce.Remove(Key(commandId, CommandPolarity.Off));

        if (polarity != CommandPolarity.Neutral)
        {
            _believed[commandId] = polarity == CommandPolarity.On;
            return;
        }

        // Une bascule sans sens annonce : on inverse ce que l'on croyait, et si l'on ne croyait
        // rien on continue de ne rien croire plutot que de deviner.
        if (_believed.TryGetValue(commandId, out bool current))
        {
            _believed[commandId] = !current;
        }
    }

    /// <summary>
    /// Oublie tout. À appeler quand le jeu redémarre : le vaisseau est reparti d'un état neuf,
    /// et une croyance héritée de la session précédente serait fausse plus souvent que juste.
    /// </summary>
    public void Forget()
    {
        _believed.Clear();
        _refusedOnce.Clear();
    }

    private static string Key(string commandId, CommandPolarity polarity) => $"{commandId}:{polarity}";
}
