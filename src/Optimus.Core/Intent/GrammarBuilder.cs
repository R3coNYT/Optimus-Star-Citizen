using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Profiles;

namespace Optimus.Core.Intent;

/// <summary>Grammaire prête à être chargée dans un moteur de reconnaissance.</summary>
/// <param name="Alternatives">Toutes les phrases que le moteur a le droit de reconnaître.</param>
/// <param name="PhraseToCommand">Correspondance phrase → identifiant de commande.</param>
/// <param name="WakeWordRequired">Le mot d'éveil est-il exigé en tête.</param>
public sealed record VoiceGrammar(
    IReadOnlyList<string> Alternatives,
    IReadOnlyDictionary<string, string> PhraseToCommand,
    bool WakeWordRequired)
{
    public int Count => Alternatives.Count;

    /// <summary>Commande désignée par une phrase reconnue, ou null si elle n'appartient pas à la grammaire.</summary>
    public string? Resolve(string recognizedText)
    {
        string normalized = TextNormalizer.Normalize(recognizedText);
        return PhraseToCommand.TryGetValue(normalized, out string? commandId) ? commandId : null;
    }
}

/// <summary>
/// Assemble la grammaire d'un copilote à partir du catalogue.
///
/// C'est ici que se joue la sécurité de l'écoute permanente. Un moteur à grammaire ne peut
/// produire qu'une des alternatives qu'on lui donne : en exigeant le mot d'éveil en tête,
/// une conversation ordinaire ne correspond à rien et se trouve rejetée <b>par construction</b>,
/// sans même avoir été transcrite. C'est ce qui rend l'écoute permanente plus sûre ici qu'elle
/// ne l'aurait été avec un transcripteur libre (décision D30).
///
/// En push-to-talk, la touche joue ce rôle de déclencheur : les deux formes sont acceptées,
/// car répéter « Optimus » alors qu'on tient déjà la touche n'ajoute qu'une syllabe de latence.
/// </summary>
public static class VoiceGrammarBuilder
{
    /// <summary>Construit la grammaire pour un catalogue, un mot d'éveil et un mode d'écoute.</summary>
    public static VoiceGrammar Build(CommandCatalog catalog, string wakeWord, VoiceInputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(wakeWord);

        string normalizedWake = TextNormalizer.Normalize(wakeWord);
        bool wakeRequired = settings.WakeWordRequired;

        List<string> alternatives = new();
        Dictionary<string, string> mapping = new(StringComparer.Ordinal);

        foreach (CommandDefinition command in catalog.Commands)
        {
            foreach (string phrase in command.VoicePhrases)
            {
                string normalized = TextNormalizer.Normalize(phrase);
                if (normalized.Length == 0)
                {
                    continue;
                }

                // Avec mot d'éveil : toujours proposé, c'est la forme naturelle.
                Add($"{normalizedWake} {normalized}", command.Id);

                // Sans mot d'éveil : uniquement quand la touche sert de déclencheur.
                if (!wakeRequired)
                {
                    Add(normalized, command.Id);
                }
            }
        }

        return new VoiceGrammar(alternatives, mapping, wakeRequired);

        void Add(string phrase, string commandId)
        {
            // Une phrase partagée par deux commandes est déjà signalée par le validateur de
            // catalogue ; ici la première l'emporte, sans quoi la grammaire serait ambiguë.
            if (mapping.ContainsKey(phrase))
            {
                return;
            }

            mapping[phrase] = commandId;
            alternatives.Add(phrase);
        }
    }
}
