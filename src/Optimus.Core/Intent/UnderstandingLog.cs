using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Optimus.Core.Intent;

/// <summary>Pourquoi Optimus n'a pas agi.</summary>
public enum HesitationKind
{
    /// <summary>Entendu, rattaché à une commande, mais sans assez de certitude pour agir.</summary>
    Proposed,

    /// <summary>Proposition refusée par le pilote : le rattachement était mauvais.</summary>
    Denied,

    /// <summary>Plusieurs commandes se valaient.</summary>
    Ambiguous,

    /// <summary>Rien de crédible.</summary>
    Unknown,
}

/// <summary>Une hésitation, agrégée sur toutes ses occurrences.</summary>
/// <param name="Heard">Ce que le moteur a rendu — la formulation la plus proche qu'il connaisse.</param>
/// <param name="Kind">Ce qui s'est passé.</param>
/// <param name="CommandId">Commande envisagée, quand il y en avait une.</param>
/// <param name="Count">Nombre d'occurrences.</param>
/// <param name="LastConfidence">Confiance de la dernière occurrence.</param>
/// <param name="LastSeen">Dernière occurrence.</param>
public sealed record Hesitation(
    string Heard,
    HesitationKind Kind,
    string? CommandId,
    int Count,
    double LastConfidence,
    DateTimeOffset LastSeen);

/// <summary>
/// Ce qu'Optimus a entendu sans agir.
///
/// <b>Une limite à connaître avant de lire cette liste</b> : la grammaire est fermée. Le moteur
/// ne peut rendre qu'une formulation qu'il connaît déjà, si bien qu'un énoncé absent du catalogue
/// ressort comme la formulation la plus proche, assortie d'une faible confiance. Optimus ne sait
/// donc <b>pas</b> ce qui a réellement été dit — il sait seulement sur quelle commande il a
/// hésité, et combien de fois.
///
/// C'est malgré tout le signal le plus utile dont on dispose : une commande sur laquelle il
/// hésite cinq fois de suite est une commande dont la formulation ne convient pas au pilote. À
/// lui d'écrire celle qu'il emploie ; Optimus ne peut que désigner l'endroit du problème.
///
/// Les occurrences sont agrégées par énoncé : trente lignes identiques n'apprendraient rien de
/// plus qu'une ligne comptée trente fois, et noieraient le reste.
/// </summary>
public sealed class UnderstandingLog
{
    /// <summary>Au-delà, les entrées les plus anciennes tombent.</summary>
    private const int Capacity = 120;

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Hésitations, les plus fréquentes d'abord.</summary>
    public IReadOnlyList<Hesitation> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values
                    .OrderByDescending(e => e.Count)
                    .ThenByDescending(e => e.LastSeen)
                    .Select(e => new Hesitation(
                        e.Heard, e.Kind, e.CommandId, e.Count, e.LastConfidence, e.LastSeen))
                    .ToList();
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Emplacement par défaut, dans les données de l'utilisateur.</summary>
    public static string DefaultPath(string profileId = "starcitizen") =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Optimus", "comprehension", $"{profileId}.json");

    /// <summary>Note une hésitation, ou incrémente celle qui existe.</summary>
    public void Record(
        string heard,
        HesitationKind kind,
        string? commandId,
        double confidence,
        TimeProvider? time = null)
    {
        if (string.IsNullOrWhiteSpace(heard))
        {
            return;
        }

        string key = $"{kind}|{heard.Trim()}|{commandId}";
        DateTimeOffset now = (time ?? TimeProvider.System).GetUtcNow();

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? existing))
            {
                existing.Count++;
                existing.LastConfidence = confidence;
                existing.LastSeen = now;
                return;
            }

            _entries[key] = new Entry
            {
                Heard = heard.Trim(),
                Kind = kind,
                CommandId = commandId,
                Count = 1,
                LastConfidence = confidence,
                LastSeen = now,
            };

            Trim();
        }
    }

    /// <summary>Retire une entrée : elle a été traitée, ou elle n'intéresse pas.</summary>
    public bool Forget(Hesitation hesitation)
    {
        ArgumentNullException.ThrowIfNull(hesitation);

        lock (_gate)
        {
            return _entries.Remove($"{hesitation.Kind}|{hesitation.Heard}|{hesitation.CommandId}");
        }
    }

    /// <summary>Vide la liste.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>Charge le journal, ou un journal vide s'il n'existe pas encore.</summary>
    public static UnderstandingLog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        UnderstandingLog log = new();

        if (!File.Exists(path))
        {
            return log;
        }

        try
        {
            Entry[]? entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(path), Format);

            foreach (Entry entry in entries ?? [])
            {
                if (!string.IsNullOrWhiteSpace(entry.Heard))
                {
                    log._entries[$"{entry.Kind}|{entry.Heard}|{entry.CommandId}"] = entry;
                }
            }
        }
        catch (JsonException)
        {
            // Un journal illisible n'est pas une raison de refuser de demarrer : c'est une aide
            // au reglage, pas une donnee de production.
        }

        return log;
    }

    /// <summary>Écrit le journal.</summary>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Entry[] snapshot;

        lock (_gate)
        {
            snapshot = _entries.Values.ToArray();
        }

        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Format));
    }

    private void Trim()
    {
        while (_entries.Count > Capacity)
        {
            string oldest = _entries
                .OrderBy(e => e.Value.LastSeen)
                .ThenBy(e => e.Value.Count)
                .First().Key;

            _entries.Remove(oldest);
        }
    }

    private sealed class Entry
    {
        [JsonPropertyName("heard")]
        public string Heard { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public HesitationKind Kind { get; set; }

        [JsonPropertyName("command_id")]
        public string? CommandId { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("last_confidence")]
        public double LastConfidence { get; set; }

        [JsonPropertyName("last_seen")]
        public DateTimeOffset LastSeen { get; set; }
    }
}
