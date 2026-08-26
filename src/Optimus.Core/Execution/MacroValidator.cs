using Optimus.Core.Domain.Bindings;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;

namespace Optimus.Core.Execution;

/// <summary>
/// Vérifie une macro avant de l'enregistrer.
///
/// Le contrôle a lieu <b>avant</b> l'écriture, jamais après : une macro incohérente écrite sur
/// disque empêcherait le catalogue entier de se charger au démarrage suivant, et le pilote se
/// retrouverait devant un Optimus muet sans savoir pourquoi. Mieux vaut refuser d'enregistrer en
/// disant ce qui cloche.
///
/// Les erreurs empêchent l'enregistrement ; les avertissements ne font que signaler.
/// </summary>
public static class MacroValidator
{
    /// <summary>Verdict d'une vérification.</summary>
    /// <param name="Errors">Ce qui empêche d'enregistrer.</param>
    /// <param name="Warnings">Ce qui mérite d'être su sans être bloquant.</param>
    public sealed record Verdict(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
    {
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Contrôle une macro dans le contexte du catalogue existant.
    /// </summary>
    /// <param name="macro">Macro à vérifier.</param>
    /// <param name="catalog">Catalogue courant, la macro elle-même y comprise ou non.</param>
    /// <param name="bindings">Profil de touches, pour repérer les pas inexécutables.</param>
    public static Verdict Check(
        CommandDefinition macro,
        CommandCatalog catalog,
        BindingProfile bindings)
    {
        ArgumentNullException.ThrowIfNull(macro);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bindings);

        List<string> errors = new();
        List<string> warnings = new();

        if (string.IsNullOrWhiteSpace(macro.Id))
        {
            errors.Add("La macro n'a pas d'identifiant.");
        }

        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            errors.Add("La macro n'a pas de nom : c'est ce qu'Optimus prononcera.");
        }

        CheckPhrases(macro, catalog, errors);
        CheckSteps(macro, catalog, bindings, errors, warnings);

        return new Verdict(errors, warnings);
    }

    private static void CheckPhrases(
        CommandDefinition macro, CommandCatalog catalog, List<string> errors)
    {
        string[] phrases = macro.AllPhrases
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

        if (phrases.Length == 0)
        {
            errors.Add("Aucune formulation : rien ne permettrait de déclencher cette macro.");
            return;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string phrase in phrases)
        {
            string normalized = TextNormalizer.Normalize(phrase);

            if (normalized.Length == 0)
            {
                errors.Add($"La formulation « {phrase} » ne contient rien de prononçable.");
                continue;
            }

            if (!seen.Add(normalized))
            {
                errors.Add($"La formulation « {phrase} » figure deux fois.");
                continue;
            }

            // Une phrase deja prise rendrait l'une des deux commandes inatteignable : la
            // grammaire ne garde qu'une correspondance par enonce.
            CommandDefinition? owner = catalog.Commands.FirstOrDefault(
                c => !string.Equals(c.Id, macro.Id, StringComparison.OrdinalIgnoreCase)
                     && c.AllPhrases.Any(p => TextNormalizer.Normalize(p) == normalized));

            if (owner is not null)
            {
                errors.Add($"« {phrase} » est déjà employée par « {owner.Name} ».");
            }
        }
    }

    private static void CheckSteps(
        CommandDefinition macro,
        CommandCatalog catalog,
        BindingProfile bindings,
        List<string> errors,
        List<string> warnings)
    {
        if (macro.Actions.Count == 0)
        {
            errors.Add("Aucune étape : la macro ne ferait rien.");
            return;
        }

        foreach (ActionStep step in macro.Actions)
        {
            switch (step.Type)
            {
                case ActionStepType.Command when step.CommandId is null
                    || !catalog.Contains(step.CommandId):
                    errors.Add($"L'étape renvoie vers « {step.CommandId} », qui n'existe pas.");
                    break;

                case ActionStepType.Wait when step.WaitMs <= 0:
                    warnings.Add("Une attente de zéro milliseconde ne sert à rien.");
                    break;

                case ActionStepType.Say when string.IsNullOrWhiteSpace(step.ResponseKey):
                    errors.Add("Une étape parlée n'indique pas quelle réplique dire.");
                    break;
            }
        }

        // Le depliage attrape les cycles et les renvois casses, y compris indirects.
        CommandCatalog withMacro = CommandCatalog.Merge(
            catalog.Id, catalog.Name, catalog, new CommandCatalog("m", "m", [macro]));

        MacroExpansion plan;

        try
        {
            plan = MacroExpander.Plan(macro, withMacro, bindings);
        }
        catch (InvalidOperationException exception)
        {
            errors.Add(exception.Message);
            return;
        }

        foreach (string reason in plan.Skipped)
        {
            warnings.Add($"Pas écarté à l'exécution : {reason}");
        }

        // Un pas sans raccourci ne fait pas echouer l'enregistrement - le pilote peut assigner
        // la touche ensuite - mais il doit le savoir maintenant plutot qu'au decollage.
        foreach (ActionStep step in plan.Steps)
        {
            if (step.Type != ActionStepType.GameAction || step.ActionId is null)
            {
                continue;
            }

            if (bindings.Resolve(step.ActionId, out _) != BindingLookup.Bound)
            {
                warnings.Add($"« {step.ActionId} » n'a pas de touche : la macro sera refusée tant "
                           + "qu'elle n'en aura pas.");
            }
        }
    }
}
