namespace Optimus.Core.Domain.Bindings;

/// <summary>Association entre une action du jeu et une entrée physique.</summary>
/// <param name="ActionId">Identifiant calqué sur Star Citizen : <c>actionmap/action</c>.</param>
/// <param name="Input">Entrée physique correspondante.</param>
/// <param name="UiLabel">Libellé du jeu, utile à l'affichage (<c>@ui_CIToggleLights</c>).</param>
/// <param name="Unsupported">Axe analogique ou périphérique que le moteur ne sait pas injecter.</param>
public sealed record Binding(
    string ActionId,
    InputSpec Input,
    string? UiLabel = null,
    bool Unsupported = false);

/// <summary>Issue de la recherche d'une action dans un profil.</summary>
public enum BindingLookup
{
    /// <summary>L'action existe et porte une entrée exploitable.</summary>
    Bound,

    /// <summary>
    /// L'action existe dans le jeu mais n'a aucune touche assignée.
    ///
    /// Cas nominal, pas exceptionnel : six commandes du catalogue MVP sont dans ce cas sur
    /// une installation neuve de la 4.9, dont l'ouverture des portes. Optimus doit le dire
    /// et proposer d'assigner la touche.
    /// </summary>
    NotBound,

    /// <summary>L'action existe mais son entrée n'est pas injectable (axe analogique, head tracking).</summary>
    Unsupported,

    /// <summary>L'action est inconnue du profil : faute de frappe, ou action retirée par une mise à jour du jeu.</summary>
    UnknownAction,
}

/// <summary>
/// Table <c>action → entrée physique</c> pour une machine et une version de jeu données.
///
/// C'est le seul endroit du système où une touche existe. Les commandes ne connaissent que
/// des identifiants d'action ; sans ce profil, elles ne peuvent rien déclencher — ce qui est
/// exactement l'objectif.
/// </summary>
public sealed class BindingProfile
{
    private readonly Dictionary<string, Binding> _bindings;
    private readonly HashSet<string> _unbound;

    public BindingProfile(
        string id,
        string name,
        string gameVersion,
        string? gameBuild,
        IEnumerable<Binding> bindings,
        IEnumerable<string> unboundActions)
    {
        Id = id;
        Name = name;
        GameVersion = gameVersion;
        GameBuild = gameBuild;

        _bindings = bindings.ToDictionary(b => b.ActionId, StringComparer.OrdinalIgnoreCase);
        _unbound = new HashSet<string>(unboundActions, StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; }

    public string Name { get; }

    public string GameVersion { get; }

    /// <summary>Build complet du jeu (<c>4.9-live.12344265</c>), repère exact pour les migrations.</summary>
    public string? GameBuild { get; }

    public int BoundCount => _bindings.Count;

    /// <summary>
    /// Profil recomposé avec les touches choisies par le pilote.
    ///
    /// C'est le « défauts ⊕ deltas » à l'œuvre : le profil du jeu reste intact — donc
    /// remplaçable à chaque mise à jour — et les choix du pilote se posent par-dessus. Une
    /// action jusque-là sans touche quitte la liste des non liées : c'est tout l'objet de
    /// l'opération, faire passer les portes du vaisseau de « aucun raccourci » à exécutable.
    /// </summary>
    public BindingProfile WithOverrides(IEnumerable<Binding> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        List<Binding> applied = overrides.ToList();
        if (applied.Count == 0)
        {
            return this;
        }

        Dictionary<string, Binding> merged = new(_bindings, StringComparer.OrdinalIgnoreCase);
        HashSet<string> stillUnbound = new(_unbound, StringComparer.OrdinalIgnoreCase);

        foreach (Binding binding in applied)
        {
            merged[binding.ActionId] = binding;
            stillUnbound.Remove(binding.ActionId);
        }

        return new BindingProfile(Id, Name, GameVersion, GameBuild, merged.Values, stillUnbound);
    }

    public int UnboundCount => _unbound.Count;

    public IReadOnlyCollection<Binding> Bindings => _bindings.Values;

    /// <summary>
    /// Résout une action. La distinction entre « non assignée » et « inconnue » est
    /// essentielle : la première appelle une invitation à configurer, la seconde signale
    /// un catalogue désynchronisé du jeu.
    /// </summary>
    public BindingLookup Resolve(string actionId, out Binding? binding)
    {
        if (_bindings.TryGetValue(actionId, out Binding? found))
        {
            binding = found;
            return found.Unsupported ? BindingLookup.Unsupported : BindingLookup.Bound;
        }

        binding = null;
        return _unbound.Contains(actionId) ? BindingLookup.NotBound : BindingLookup.UnknownAction;
    }

    /// <summary>Profil vide, utile aux tests et au tout premier démarrage.</summary>
    public static BindingProfile Empty { get; } =
        new("empty", "Aucun profil", "0", null, Array.Empty<Binding>(), Array.Empty<string>());
}
