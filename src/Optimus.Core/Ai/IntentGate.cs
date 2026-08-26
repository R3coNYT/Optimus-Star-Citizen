using System.Text.Json;
using Optimus.Core.Domain.Commands;

namespace Optimus.Core.Ai;

/// <summary>
/// Les cinq verrous appliqués à toute réponse du modèle, dans l'ordre (docs/07.5).
///
/// C'est la pièce qui rend l'étage conversationnel acceptable, et elle est délibérément
/// ennuyeuse : elle ne fait qu'analyser du texte et comparer des identifiants. Elle n'a accès ni
/// au moteur d'entrée, ni au profil de touches, ni à quoi que ce soit qui puisse appuyer sur
/// quelque chose. Un modèle qui répondrait <c>{"intent": "format C:"}</c> obtiendrait exactement
/// le même résultat qu'un modèle muet : un refus journalisé.
///
/// <list type="number">
/// <item>La réponse est du JSON conforme, sinon rejet.</item>
/// <item>L'intent figure dans la <b>liste blanche</b> — le catalogue — sinon rejet journalisé.</item>
/// <item>Les paramètres respectent ce que la commande déclare.</item>
/// <item>Le garde d'exécution s'applique ensuite normalement, ailleurs et sans exception.</item>
/// <item>La confiance est <b>plafonnée</b> : elle ne peut jamais dispenser une commande
/// dangereuse de sa confirmation.</item>
/// </list>
///
/// Les verrous 1 à 3 et 5 vivent ici. Le quatrième est <see cref="Execution.ExecutionGuard"/>,
/// que rien ne contourne — une décision issue du modèle emprunte exactement le même chemin
/// d'exécution qu'une commande dite au micro.
/// </summary>
public static class IntentGate
{
    /// <summary>
    /// Confiance maximale accordée à une proposition du modèle.
    ///
    /// Volontairement sous le seuil d'exécution directe : une intention devinée par un modèle
    /// n'a pas à valoir autant qu'une phrase reconnue mot pour mot dans le catalogue.
    /// </summary>
    public const double ConfidenceCeiling = 0.90;

    /// <summary>Applique les verrous à la réponse brute du modèle.</summary>
    /// <param name="raw">Texte rendu par le modèle, censé être du JSON.</param>
    /// <param name="catalog">Liste blanche : rien d'autre n'est exécutable.</param>
    public static AiDecision Apply(string? raw, CommandCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return AiDecision.Refused(AiRejection.NoAnswer);
        }

        JsonElement root;

        try
        {
            // Certains modeles encadrent le JSON de texte ou de balises de code malgre la
            // consigne. On isole l'objet plutot que de rejeter pour un defaut de forme.
            using JsonDocument document = JsonDocument.Parse(Isolate(raw));
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return AiDecision.Refused(AiRejection.Malformed, Truncate(raw));
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return AiDecision.Refused(AiRejection.Malformed, Truncate(raw));
        }

        string? reasoning = Text(root, "reasoning");

        return Text(root, "type")?.ToLowerInvariant() switch
        {
            "command" => Command(root, catalog, reasoning),
            "conversation" => Conversation(root, reasoning),
            "clarification" => Clarification(root, reasoning),
            _ => AiDecision.Refused(AiRejection.Malformed, reasoning ?? Truncate(raw)),
        };
    }

    private static AiDecision Command(JsonElement root, CommandCatalog catalog, string? reasoning)
    {
        string? intent = Text(root, "intent");

        // Verrou 2 : hors du catalogue, rien n'existe. C'est le seul chemin vers l'execution,
        // et il ne s'elargit pas parce qu'un modele l'a demande.
        if (string.IsNullOrWhiteSpace(intent)
            || !catalog.TryGet(intent, out CommandDefinition? command)
            || command is null)
        {
            return AiDecision.Refused(AiRejection.UnknownIntent, intent ?? reasoning);
        }

        CommandPolarity polarity = Text(root, "polarity")?.ToLowerInvariant() switch
        {
            "on" => CommandPolarity.On,
            "off" => CommandPolarity.Off,
            _ => CommandPolarity.Neutral,
        };

        // Verrou 3 : un sens demandé que la commande ne sait pas exprimer n'est pas une erreur
        // fatale, mais il ne doit pas être transmis tel quel - il retomberait sur une bascule.
        if (polarity != CommandPolarity.Neutral && !command.HasPolarity)
        {
            polarity = CommandPolarity.Neutral;
        }

        double confidence = Math.Clamp(Number(root, "confidence") ?? 0.5, 0, ConfidenceCeiling);

        // Verrou 5 : une commande dangereuse exige sa confirmation, quoi que le modele annonce.
        bool confirm = command.Dangerous || Boolean(root, "requires_confirmation") == true;

        return new AiDecision(
            AiDecisionKind.Command, command.Id, polarity, confidence, confirm,
            Reasoning: reasoning);
    }

    private static AiDecision Conversation(JsonElement root, string? reasoning)
    {
        string? reply = Text(root, "reply") ?? Text(root, "reply_hint");

        return string.IsNullOrWhiteSpace(reply)
            ? AiDecision.Refused(AiRejection.Malformed, reasoning)
            : new AiDecision(AiDecisionKind.Conversation, Reply: reply.Trim(), Reasoning: reasoning);
    }

    private static AiDecision Clarification(JsonElement root, string? reasoning)
    {
        string? question = Text(root, "question") ?? Text(root, "question_key");

        return string.IsNullOrWhiteSpace(question)
            ? AiDecision.Refused(AiRejection.Malformed, reasoning)
            : new AiDecision(
                AiDecisionKind.Clarification, Question: question.Trim(), Reasoning: reasoning);
    }

    /// <summary>Isole le premier objet JSON du texte, balises de code comprises.</summary>
    private static string Isolate(string raw)
    {
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');

        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static bool? Boolean(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private static string Truncate(string text) =>
        text.Length <= 200 ? text : text[..200] + "…";
}
