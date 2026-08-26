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

        // Le controle parcourt les DEUX branches de chaque « si », la ou le depliage n'en retient
        // qu'une. Une branche jamais prise aujourd'hui - parce que la touche manque, parce que le
        // vaisseau est en NAV - sera prise demain, et un renvoi casse qui s'y cache empecherait
        // alors le catalogue entier de se charger.
        bool testsBelief = false;

        Inspect(macro.Actions);

        if (testsBelief)
        {
            warnings.Add(
                "Cette macro se fie à un état supposé. Optimus ne connaît que les commutations "
                + "qu'il a lui-même provoquées : si vous avez actionné la même fonction au "
                + "clavier, il se trompera sans le savoir.");
        }

        void Inspect(IReadOnlyList<ActionStep> steps)
        {
            foreach (ActionStep step in steps)
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

                    case ActionStepType.If:
                        CheckCondition(step.Condition);
                        Inspect(step.Block);
                        Inspect(step.Alternative);

                        if (step.Block.Count == 0 && step.Alternative.Count == 0)
                        {
                            warnings.Add("Un « si » dont les deux branches sont vides ne fait rien.");
                        }

                        break;

                    case ActionStepType.Repeat:
                        if (step.Repeat < 1 || step.Repeat > MacroExpander.MaxRepeat)
                        {
                            errors.Add($"Une répétition de {step.Repeat} tours : le compte doit être "
                                     + $"compris entre 1 et {MacroExpander.MaxRepeat}.");
                        }

                        if (step.Block.Count == 0)
                        {
                            errors.Add("Une répétition sans étapes ne ferait que perdre du temps.");
                        }

                        Inspect(step.Block);
                        break;
                }
            }
        }

        void CheckCondition(MacroCondition? condition)
        {
            if (condition is null)
            {
                errors.Add("Un « si » sans condition : rien ne permettrait de trancher.");
                return;
            }

            if (condition.Subject is ConditionSubject.Binding or ConditionSubject.Directed
                or ConditionSubject.Believed)
            {
                if (condition.CommandId is null || !catalog.Contains(condition.CommandId))
                {
                    errors.Add($"La condition porte sur « {condition.CommandId} », qui n'existe pas.");
                }
            }

            switch (condition.Subject)
            {
                case ConditionSubject.FlightMode
                    when !IsOneOf(condition.Value, "nav", "scm"):
                    errors.Add($"Un mode de vol « {condition.Value} » : attendu « nav » ou « scm ».");
                    break;

                case ConditionSubject.Believed when !IsOneOf(condition.Value, "on", "off"):
                    errors.Add($"Un état supposé « {condition.Value} » : attendu « on » ou « off ».");
                    break;

                case ConditionSubject.Believed:
                    testsBelief = true;
                    break;

                case ConditionSubject.Directed when condition.Polarity == CommandPolarity.Neutral:
                    errors.Add("Une condition sur le sens dirigé doit dire lequel : « on » ou « off ».");
                    break;
            }
        }

        static bool IsOneOf(string? value, params string[] allowed) =>
            value is not null
            && allowed.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

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
