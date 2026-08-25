using System.Text.Json;
using System.Text.Json.Serialization;
using Optimus.Core.Domain.Bindings;

namespace Optimus.Core.Bindings;

/// <summary>Origine d'une assignation, pour savoir ce qu'on a le droit d'écraser.</summary>
public enum AssignmentOrigin
{
    /// <summary>Assignée à la main dans l'éditeur d'Optimus.</summary>
    Manual,

    /// <summary>Reprise d'un fichier de mappage exporté du jeu.</summary>
    ImportedLayout,
}

/// <summary>Une touche que le pilote a choisie pour une action.</summary>
/// <param name="ActionId">Action du jeu.</param>
/// <param name="Input">Entrée physique.</param>
/// <param name="Origin">D'où vient cette assignation.</param>
/// <param name="AssignedAt">Quand elle a été posée.</param>
public sealed record BindingAssignment(
    string ActionId,
    InputSpec Input,
    AssignmentOrigin Origin,
    DateTimeOffset AssignedAt);

/// <summary>
/// Les touches choisies par le pilote, superposées au profil par défaut.
///
/// Le profil du jeu n'est jamais modifié : il décrit la 4.9 telle que Cloud Imperium la livre,
/// et doit pouvoir être remplacé à chaque mise à jour sans rien perdre. Ce qui appartient au
/// pilote vit ici, dans un fichier distinct — c'est le « ⊕ deltas » du modèle, et c'est ce qui
/// permet de réimporter un nouveau <c>defaultProfile</c> sans effacer son travail.
///
/// Rien de tout cela n'apprend quoi que ce soit à Star Citizen : voir <see cref="ScLayoutXml"/>
/// pour l'autre moitié du chemin, sans laquelle une assignation reste lettre morte.
/// </summary>
public sealed class BindingOverlay
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Dictionary<string, BindingAssignment> _assignments =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Assignations, triées par action.</summary>
    public IReadOnlyList<BindingAssignment> Assignments =>
        _assignments.Values.OrderBy(a => a.ActionId, StringComparer.Ordinal).ToList();

    public int Count => _assignments.Count;

    /// <summary>Touche choisie pour cette action, s'il y en a une.</summary>
    public BindingAssignment? Find(string actionId) =>
        _assignments.TryGetValue(actionId, out BindingAssignment? found) ? found : null;

    /// <summary>Pose ou remplace une assignation.</summary>
    public void Assign(string actionId, InputSpec input, AssignmentOrigin origin, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(input);

        _assignments[actionId] = new BindingAssignment(
            actionId, input, origin, (time ?? TimeProvider.System).GetUtcNow());
    }

    /// <summary>Retire une assignation. Vrai si elle existait.</summary>
    public bool Remove(string actionId) => _assignments.Remove(actionId);

    /// <summary>
    /// Actions dont la touche entre en conflit avec une autre assignation.
    ///
    /// Deux actions sur la même touche n'est pas interdit — Star Citizen le fait lui-même entre
    /// contextes différents, un vaisseau et un personnage ne partageant pas leurs commandes.
    /// Mais c'est presque toujours une erreur quand on assigne à la main, donc on le signale.
    /// </summary>
    public IReadOnlyList<(string First, string Second, InputSpec Input)> Conflicts()
    {
        List<(string, string, InputSpec)> conflicts = new();
        Dictionary<string, string> byCombination = new(StringComparer.OrdinalIgnoreCase);

        foreach (BindingAssignment assignment in Assignments)
        {
            string combination = Combination(assignment.Input);

            if (byCombination.TryGetValue(combination, out string? other))
            {
                conflicts.Add((other, assignment.ActionId, assignment.Input));
                continue;
            }

            byCombination[combination] = assignment.ActionId;
        }

        return conflicts;
    }

    /// <summary>Signature d'une combinaison, pour comparer deux entrées.</summary>
    public static string Combination(InputSpec input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Modifiers.Count == 0
            ? input.Key
            : string.Join('+', input.Modifiers.OrderBy(m => m, StringComparer.Ordinal).Append(input.Key));
    }

    /// <summary>Charge le fichier d'assignations, ou une couche vide s'il n'existe pas encore.</summary>
    public static BindingOverlay Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        BindingOverlay overlay = new();

        if (!File.Exists(path))
        {
            return overlay;
        }

        OverlayFile? file = JsonSerializer.Deserialize<OverlayFile>(File.ReadAllText(path), Json);

        foreach (AssignmentRecord record in file?.Assignments ?? [])
        {
            if (string.IsNullOrWhiteSpace(record.ActionId) || string.IsNullOrWhiteSpace(record.Key))
            {
                continue;
            }

            overlay._assignments[record.ActionId] = new BindingAssignment(
                record.ActionId,
                new InputSpec(
                    record.Key,
                    record.Modifiers ?? [],
                    record.Device,
                    record.Mode,
                    record.HoldMs ?? InputDefaults.HoldMs),
                record.Origin,
                record.AssignedAt);
        }

        return overlay;
    }

    /// <summary>Écrit le fichier d'assignations, en créant le dossier au besoin.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        OverlayFile file = new()
        {
            Note = "Touches choisies par le pilote. Le profil du jeu n'est jamais modifié : "
                 + "il peut donc être réimporté à chaque mise à jour sans rien perdre d'ici.",
            Assignments = Assignments.Select(a => new AssignmentRecord
            {
                ActionId = a.ActionId,
                Key = a.Input.Key,
                Modifiers = a.Input.Modifiers.Count == 0 ? null : a.Input.Modifiers.ToArray(),
                Device = a.Input.Device,
                Mode = a.Input.Mode,
                HoldMs = a.Input.Mode == InputMode.Hold ? a.Input.HoldMs : null,
                Origin = a.Origin,
                AssignedAt = a.AssignedAt,
            }).ToArray(),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(file, Json));
    }

    /// <summary>
    /// Emplacement par défaut du fichier d'assignations.
    ///
    /// Dans les données de l'utilisateur, jamais à côté de l'exécutable : le script de
    /// publication recopie tout le dossier <c>data/</c>, et le travail du pilote y serait effacé
    /// à la première mise à jour.
    /// </summary>
    public static string DefaultPath(string profileId = "starcitizen") =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Optimus", "bindings", $"{profileId}.json");

    private sealed class OverlayFile
    {
        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("assignments")]
        public AssignmentRecord[]? Assignments { get; set; }
    }

    private sealed class AssignmentRecord
    {
        [JsonPropertyName("action_id")]
        public string? ActionId { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("modifiers")]
        public string[]? Modifiers { get; set; }

        [JsonPropertyName("device")]
        public InputDevice Device { get; set; }

        [JsonPropertyName("mode")]
        public InputMode Mode { get; set; }

        [JsonPropertyName("hold_ms")]
        public int? HoldMs { get; set; }

        [JsonPropertyName("origin")]
        public AssignmentOrigin Origin { get; set; }

        [JsonPropertyName("assigned_at")]
        public DateTimeOffset AssignedAt { get; set; }
    }
}
