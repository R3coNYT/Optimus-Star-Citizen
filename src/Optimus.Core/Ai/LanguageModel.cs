using System.Text;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Domain.Copilots;

namespace Optimus.Core.Ai;

/// <summary>Ce qu'on demande au modèle.</summary>
/// <param name="System">Consigne : rôle, format attendu, liste blanche.</param>
/// <param name="User">Ce que le pilote a dit.</param>
/// <param name="JsonOnly">Exiger du JSON, quand le fournisseur sait le contraindre.</param>
/// <param name="Temperature">Basse pour trancher une intention, plus haute pour converser.</param>
/// <param name="MaxTokens">Plafond de la réponse.</param>
public sealed record LanguageRequest(
    string System,
    string User,
    bool JsonOnly = true,
    double Temperature = 0.2,
    int MaxTokens = 300);

/// <summary>
/// Un modèle de langage, quel qu'il soit.
///
/// Volontairement minuscule : du texte entre, du texte sort. Le modèle ne voit ni le profil de
/// touches, ni le moteur d'entrée, et ne peut rien déclencher — sa réponse traverse
/// <see cref="IntentGate"/> avant que quiconque en fasse quoi que ce soit.
///
/// Facultatif par construction (§84). Sans fournisseur configuré, Optimus fonctionne exactement
/// comme avant : le catalogue et la grammaire suffisent, et rien ne part sur le réseau.
/// </summary>
public interface ILanguageModel : IAsyncDisposable
{
    /// <summary>Identifiant lisible, pour les journaux et l'interface.</summary>
    string Id { get; }

    /// <summary>Vrai si le fournisseur répond. Vérifié sans consommer de jetons.</summary>
    Task<bool> IsReachableAsync(CancellationToken cancellationToken = default);

    /// <summary>Interroge le modèle. Retourne <c>null</c> en cas d'échec, jamais une exception.</summary>
    Task<string?> CompleteAsync(LanguageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Construit les consignes envoyées au modèle.
///
/// La liste blanche <b>est</b> l'invite : le modèle ne connaît d'Optimus que les commandes qu'on
/// lui énumère, et le verrou 2 refusera de toute façon tout ce qui n'y figure pas. Lui décrire
/// autre chose serait au mieux inutile, au pire une invitation à inventer.
/// </summary>
public static class AiPrompt
{
    /// <summary>
    /// Nombre de commandes décrites au modèle.
    ///
    /// Le catalogue entier ferait une invite considérable, relue à chaque appel. On s'en tient
    /// aux commandes actives, ce qui suffit largement : le rôle du modèle est de rattacher une
    /// tournure inhabituelle à une commande, pas d'explorer un référentiel.
    /// </summary>
    private const int MaxCommands = 90;

    /// <summary>Consigne pour rattacher un énoncé à une commande, ou converser.</summary>
    public static string Resolve(CommandCatalog catalog, Copilot copilot)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(copilot);

        StringBuilder prompt = new();

        prompt.AppendLine($"Tu es {copilot.Name}, copilote de bord d'un vaisseau de Star Citizen.");
        prompt.AppendLine("Le pilote vient de te parler. Tu dois décider ce qu'il attend.");
        prompt.AppendLine();
        prompt.AppendLine("Réponds UNIQUEMENT par un objet JSON, sans texte autour, dans l'une de ces formes :");
        prompt.AppendLine();
        prompt.AppendLine("""{"type":"command","intent":"<identifiant>","polarity":"on|off|none","confidence":0.0,"requires_confirmation":false,"reasoning":"<court>"}""");
        prompt.AppendLine("""{"type":"conversation","reply":"<ta réponse parlée, une ou deux phrases>","reasoning":"<court>"}""");
        prompt.AppendLine("""{"type":"clarification","question":"<ce que tu demandes>","reasoning":"<court>"}""");
        prompt.AppendLine();
        prompt.AppendLine("Règles :");
        prompt.AppendLine("- « intent » DOIT être l'un des identifiants listés plus bas, à l'identique. Aucun autre n'existe.");
        prompt.AppendLine("- Si l'énoncé ne correspond à aucune commande, réponds « conversation ».");
        prompt.AppendLine("- « polarity » vaut « on » pour allumer/ouvrir/sortir, « off » pour éteindre/fermer/rentrer, sinon « none ».");
        prompt.AppendLine("- Ne propose jamais une commande dont tu n'es pas raisonnablement sûr : « conversation » vaut mieux qu'une erreur.");
        prompt.AppendLine("- Tes réponses parlées sont brèves, en français, dans le ton d'un copilote militaire calme.");
        prompt.AppendLine();
        prompt.AppendLine("Commandes disponibles :");

        foreach (CommandDefinition command in catalog.Commands
            .OrderBy(c => c.Category, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .Take(MaxCommands))
        {
            string sample = command.AllPhrases.FirstOrDefault() ?? command.Name;
            string sense = command.HasPolarity ? " [sens possible]" : string.Empty;

            prompt.AppendLine($"  {command.Id} — {command.Name} (ex. « {sample} »){sense}");
        }

        return prompt.ToString();
    }

    /// <summary>Consigne pour une réplique de dialogue, sans aucune exécution possible.</summary>
    public static string Converse(Copilot copilot)
    {
        ArgumentNullException.ThrowIfNull(copilot);

        Domain.Personality.PersonalityTraits traits = copilot.Personality.Traits;

        return $$"""
            Tu es {{copilot.Name}}, copilote de bord d'un vaisseau de Star Citizen.
            {{copilot.Description}}

            Ton caractère, sur cent : formalité {{traits.Formality}}, humour {{traits.Humor}},
            ironie {{traits.Sarcasm}}, chaleur {{traits.Warmth}}, calme {{traits.Calmness}}.

            Réponds en français, en {{traits.MaxWords}} mots au maximum, sans emoji ni mise en forme.
            Tu es à bord, tu t'adresses au pilote. Tu ne prétends jamais connaître l'état du
            vaisseau : tu n'as aucune télémétrie, et inventer un relevé serait pire que de
            l'admettre.

            Réponds UNIQUEMENT par : {"type":"conversation","reply":"<ta réponse>"}
            """;
    }
}
