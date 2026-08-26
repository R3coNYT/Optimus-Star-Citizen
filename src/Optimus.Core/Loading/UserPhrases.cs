using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;

namespace Optimus.Core.Loading;

/// <summary>Une formulation ajoutée par le pilote à une commande existante.</summary>
/// <param name="CommandId">Commande visée.</param>
/// <param name="Phrase">Formulation, telle qu'elle sera prononcée — accents compris.</param>
/// <param name="Polarity">Sens, quand la formulation en porte un.</param>
/// <param name="AddedAt">Quand elle a été ajoutée.</param>
public sealed record PhraseAlias(
    string CommandId,
    string Phrase,
    CommandPolarity Polarity,
    DateTimeOffset AddedAt);

/// <summary>
/// Les formulations que le pilote ajoute aux commandes livrées.
///
/// Hors du catalogue, comme les macros et les touches, et pour la même raison : le dossier
/// <c>data/</c> est remplacé à chaque publication. Une formulation écrite dedans disparaîtrait
/// à la mise à jour suivante, silencieusement.
///
/// C'est ce qui rend le réglage de la reconnaissance <b>cumulatif</b>. Chaque fois qu'Optimus
/// hésite, le pilote peut lui apprendre la tournure qu'il emploie réellement, et cet
/// apprentissage lui appartient — il survit aux mises à jour et se sauvegarde en copiant un
/// fichier.
/// </summary>
public static class UserPhrases
{
    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Emplacement par défaut, dans les données de l'utilisateur.</summary>
    public static string DefaultPath(string profileId = "starcitizen") =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Optimus", "formulations", $"{profileId}.json");

    /// <summary>Charge les formulations ajoutées, ou rien si le fichier n'existe pas encore.</summary>
    public static IReadOnlyList<PhraseAlias> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            Record[]? records = JsonSerializer.Deserialize<Record[]>(File.ReadAllText(path), Format);

            return (records ?? [])
                .Where(r => !string.IsNullOrWhiteSpace(r.CommandId) && !string.IsNullOrWhiteSpace(r.Phrase))
                .Select(r => new PhraseAlias(r.CommandId!, r.Phrase!, r.Polarity, r.AddedAt))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Écrit les formulations ajoutées.</summary>
    public static void Save(string path, IEnumerable<PhraseAlias> aliases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(aliases);

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Record[] records = aliases
            .OrderBy(a => a.CommandId, StringComparer.Ordinal)
            .ThenBy(a => a.Phrase, StringComparer.Ordinal)
            .Select(a => new Record
            {
                CommandId = a.CommandId,
                Phrase = a.Phrase,
                Polarity = a.Polarity,
                AddedAt = a.AddedAt,
            })
            .ToArray();

        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(records, Format));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Recompose le catalogue en y ajoutant les formulations du pilote.
    ///
    /// Une formulation visant une commande inconnue est ignorée plutôt que de faire échouer le
    /// chargement : un catalogue peut perdre une commande d'une version à l'autre, et cela ne
    /// justifie pas qu'Optimus refuse de démarrer.
    /// </summary>
    public static CommandCatalog Apply(CommandCatalog catalog, IReadOnlyList<PhraseAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(aliases);

        if (aliases.Count == 0)
        {
            return catalog;
        }

        Dictionary<string, List<PhraseAlias>> byCommand = aliases
            .GroupBy(a => a.CommandId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        List<CommandDefinition> rebuilt = new();

        foreach (CommandDefinition command in catalog.Commands)
        {
            if (!byCommand.TryGetValue(command.Id, out List<PhraseAlias>? extra))
            {
                rebuilt.Add(command);
                continue;
            }

            rebuilt.Add(command with
            {
                VoicePhrases = Merge(command.VoicePhrases, extra, CommandPolarity.Neutral),
                PhrasesOn = Merge(command.PhrasesOn, extra, CommandPolarity.On),
                PhrasesOff = Merge(command.PhrasesOff, extra, CommandPolarity.Off),
            });
        }

        return new CommandCatalog(catalog.Id, catalog.Name, rebuilt);
    }

    private static IReadOnlyList<string> Merge(
        IReadOnlyList<string> existing, List<PhraseAlias> aliases, CommandPolarity polarity)
    {
        string[] added = aliases
            .Where(a => a.Polarity == polarity)
            .Select(a => a.Phrase)
            .ToArray();

        if (added.Length == 0)
        {
            return existing;
        }

        // Une formulation deja presente ne se duplique pas : elle serait indexee deux fois, et
        // seule la premiere compterait.
        HashSet<string> seen = new(
            existing.Select(TextNormalizer.Normalize), StringComparer.Ordinal);

        List<string> merged = new(existing);

        foreach (string phrase in added)
        {
            if (seen.Add(TextNormalizer.Normalize(phrase)))
            {
                merged.Add(phrase);
            }
        }

        return merged;
    }

    private sealed class Record
    {
        [JsonPropertyName("command_id")]
        public string? CommandId { get; set; }

        [JsonPropertyName("phrase")]
        public string? Phrase { get; set; }

        [JsonPropertyName("polarity")]
        public CommandPolarity Polarity { get; set; }

        [JsonPropertyName("added_at")]
        public DateTimeOffset AddedAt { get; set; }
    }
}
