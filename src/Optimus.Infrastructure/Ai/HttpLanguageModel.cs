using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Optimus.Core.Ai;
using Optimus.Core.Diagnostics;

namespace Optimus.Infrastructure.Ai;

/// <summary>
/// Modèle de langage joignable en HTTP.
///
/// Deux dialectes couvrent l'essentiel : celui d'Ollama, et celui de l'API OpenAI que reprennent
/// LM Studio, llama.cpp, vLLM, Groq, et bien d'autres. Le premier est le défaut, et ce n'est pas
/// un hasard : il tourne sur la machine du pilote, ce qui garde Optimus entièrement local même
/// quand l'étage conversationnel est actif.
///
/// <b>Aucune exception ne sort d'ici.</b> Un service arrêté, un modèle absent, une coupure
/// réseau : tout cela rend <c>null</c>, et Optimus retombe sur son catalogue. Un copilote qui
/// tomberait parce qu'un serveur local ne répond plus serait un mauvais échange — le chemin
/// rapide n'a jamais eu besoin de lui.
/// </summary>
public sealed class HttpLanguageModel : ILanguageModel
{
    /// <summary>
    /// Au-delà, on abandonne.
    ///
    /// Généreux à dessein : un modèle local sur processeur met plusieurs secondes, et l'étage
    /// conversationnel n'intervient qu'après l'échec du chemin rapide — personne n'attend une
    /// réponse instantanée à ce stade.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Delai pour <b>etablir</b> la connexion, distinct du precedent.
    ///
    /// Mesure du 2026-08-26 : sans borne, un port ferme sur `localhost` coute <b>4,1 s</b> avant
    /// de rendre la main, la pile reessayant sur plusieurs adresses. C'est le cas le plus
    /// frequent — Ollama n'est pas lance — et quatre secondes de silence avant un « je ne
    /// comprends pas » se paient au moment ou le pilote attend une reponse. Un service qui
    /// ecoute, lui, accepte en quelques millisecondes, qu'il soit local ou distant : deux
    /// secondes sont largement au-dessus du besoin. La generation garde ses 45 s.
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly AiSettings _settings;
    private readonly bool _ollama;

    public HttpLanguageModel(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _ollama = settings.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

        SocketsHttpHandler handler = new()
        {
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        _http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };

        if (Environment.GetEnvironmentVariable("OPTIMUS_AI_KEY") is string key
            && !string.IsNullOrWhiteSpace(key))
        {
            // La clé passe par l'environnement, jamais par un fichier de configuration : elle
            // n'a rien à faire dans un dossier qu'on copie sur une clé USB.
            _http.DefaultRequestHeaders.Authorization = new("Bearer", key);
        }
    }

    public string Id => $"{_settings.Provider}:{_settings.Model}";

    public async Task<bool> IsReachableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string probe = _ollama
                ? $"{_settings.Endpoint.TrimEnd('/')}/api/tags"
                : $"{_settings.Endpoint.TrimEnd('/')}/v1/models";

            using HttpResponseMessage response = await _http
                .GetAsync(probe, cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<string?> CompleteAsync(
        LanguageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        long start = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            string url = _ollama
                ? $"{_settings.Endpoint.TrimEnd('/')}/api/chat"
                : $"{_settings.Endpoint.TrimEnd('/')}/v1/chat/completions";

            using HttpResponseMessage response = await _http
                .PostAsJsonAsync(url, Body(request), cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                DiagnosticLog.Warn(
                    $"le modèle a répondu {(int)response.StatusCode}",
                    await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

                return null;
            }

            string payload = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return Extract(payload);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // La duree mesuree, et non la constante : les deux delais aboutissent ici, et
            // annoncer 45 s pour un abandon survenu au bout de 2 laisserait chercher une lenteur
            // du modele la ou rien n'ecoutait.
            DiagnosticLog.Warn(
                $"le modèle n'a pas répondu — abandon après {Elapsed(start):F1} s",
                _settings.Endpoint);

            return null;
        }
        catch (Exception exception)
        {
            DiagnosticLog.Warn(
                $"modèle injoignable ({Id}) après {Elapsed(start):F1} s", exception.Message);

            return null;
        }
    }

    private static double Elapsed(long start) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - start)
        / (double)System.Diagnostics.Stopwatch.Frequency;

    private JsonObject Body(LanguageRequest request)
    {
        JsonArray messages =
        [
            new JsonObject { ["role"] = "system", ["content"] = request.System },
            new JsonObject { ["role"] = "user", ["content"] = request.User },
        ];

        if (_ollama)
        {
            JsonObject options = new()
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxTokens,
            };

            JsonObject body = new()
            {
                ["model"] = _settings.Model,
                ["messages"] = messages,
                ["stream"] = false,
                ["options"] = options,
            };

            if (request.JsonOnly)
            {
                // Ollama contraint la sortie a du JSON valide : le premier verrou s'en trouve
                // presque toujours satisfait, sans rien lui retirer de sa raison d'etre.
                body["format"] = "json";
            }

            return body;
        }

        JsonObject openAi = new()
        {
            ["model"] = _settings.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
        };

        if (request.JsonOnly)
        {
            openAi["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        return openAi;
    }

    /// <summary>Extrait le texte de la réponse, selon le dialecte.</summary>
    private string? Extract(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            if (_ollama)
            {
                return root.TryGetProperty("message", out JsonElement message)
                       && message.TryGetProperty("content", out JsonElement content)
                    ? content.GetString()
                    : null;
            }

            return root.TryGetProperty("choices", out JsonElement choices)
                   && choices.GetArrayLength() > 0
                   && choices[0].TryGetProperty("message", out JsonElement first)
                   && first.TryGetProperty("content", out JsonElement text)
                ? text.GetString()
                : null;
        }
        catch (JsonException exception)
        {
            DiagnosticLog.Warn("réponse du modèle illisible", exception.Message);
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
