namespace Optimus.Core.Domain.Commands;

/// <summary>
/// Ensemble des commandes connues d'Optimus.
///
/// Le catalogue est aussi la <b>liste blanche</b> évoquée dans docs/07 : un intent produit par
/// un LLM n'est exécutable que s'il désigne une commande présente ici. Il n'existe aucun autre
/// chemin vers l'exécution.
/// </summary>
public sealed class CommandCatalog
{
    private readonly Dictionary<string, CommandDefinition> _commands;

    public CommandCatalog(string id, string name, IEnumerable<CommandDefinition> commands)
    {
        Id = id;
        Name = name;
        _commands = commands.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
    }

    public string Id { get; }

    public string Name { get; }

    public int Count => _commands.Count;

    public IReadOnlyCollection<CommandDefinition> Commands => _commands.Values;

    public bool TryGet(string commandId, out CommandDefinition? command) =>
        _commands.TryGetValue(commandId, out command);

    /// <summary>Vrai si l'identifiant désigne une commande connue. Point d'entrée de la validation des intents.</summary>
    public bool Contains(string commandId) => _commands.ContainsKey(commandId);

    public IEnumerable<CommandDefinition> ByCategory(string category) =>
        _commands.Values.Where(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fusionne plusieurs catalogues ; en cas de collision, le dernier chargé l'emporte.</summary>
    public static CommandCatalog Merge(string id, string name, params CommandCatalog[] catalogs)
    {
        Dictionary<string, CommandDefinition> merged = new(StringComparer.OrdinalIgnoreCase);
        foreach (CommandCatalog catalog in catalogs)
        {
            foreach (CommandDefinition command in catalog.Commands)
            {
                merged[command.Id] = command;
            }
        }

        return new CommandCatalog(id, name, merged.Values);
    }

    public static CommandCatalog Empty { get; } =
        new("empty", "Catalogue vide", Array.Empty<CommandDefinition>());
}
