using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Optimus.Core.Api;
using Optimus.Core.Diagnostics;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Abstractions;
using Optimus.Core.Execution;
using Optimus.Core.Intent;
using Optimus.Infrastructure.Hosting;

namespace Optimus.Infrastructure.Api;

/// <summary>
/// Une requête que le client a mal formée : champ absent, commande inconnue.
///
/// Distinguée d'une panne du serveur parce que le remède n'est pas le même : sur un 500 on
/// cherche un défaut dans Optimus, sur un 400 on relit sa requête. Confondre les deux enverrait
/// tous les auteurs de clients chercher au mauvais endroit.
/// </summary>
public sealed class ApiRequestException(string message) : Exception(message);

/// <summary>
/// L'API locale d'Optimus.
///
/// <b>Elle n'écoute que <c>127.0.0.1</c>, et Windows le garantit mieux que nous.</b> Mesuré le
/// 2026-08-27 : <c>HttpListener</c> accepte <c>http://127.0.0.1:port/</c> sans aucun privilège,
/// mais refuse <c>http://+:port/</c> — écoute sur toutes les interfaces — à qui n'est pas
/// administrateur. Optimus s'installant par utilisateur, sans UAC, il ne peut donc <i>pas</i>
/// s'exposer au réseau local, même par erreur de programmation. La promesse §81-83 est portée
/// par le système, pas seulement par une ligne de code qu'un jour quelqu'un modifierait.
///
/// <b>Elle transporte des intentions, jamais des touches.</b> Aucune route ne prend un code de
/// touche : on désigne une commande du catalogue, exactement comme le fait l'étage
/// conversationnel (verrou 2 de docs/07.5). Ce qui s'exécute repasse par
/// <see cref="ExecutionGuard"/> — simulation, arrêt d'urgence, jeu au premier plan,
/// confirmation des commandes dangereuses. Il n'existe pas de voie réservée.
///
/// <c>HttpListener</c> plutôt que Kestrel : une poignée de routes sur la boucle locale ne
/// justifie pas d'embarquer ASP.NET Core dans une publication autonome déjà à 76 Mo.
/// </summary>
public sealed class LocalApiServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly OptimusRuntime _runtime;
    private readonly ApiSettings _settings;
    private readonly IReadOnlyList<ApiToken> _tokens;
    private readonly ApiRateLimiter _limiter;
    private readonly List<WebSocket> _sockets = new();
    private readonly SemaphoreSlim _socketLock = new(1, 1);

    private HttpListener? _listener;
    private CancellationTokenSource? _stopping;
    private Task? _loop;

    public LocalApiServer(
        OptimusRuntime runtime, ApiSettings settings, IReadOnlyList<ApiToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tokens);

        _runtime = runtime;
        _settings = settings;
        _tokens = tokens;
        _limiter = new ApiRateLimiter(settings.ExecutionsPerMinute);
    }

    /// <summary>Vrai tant que le serveur accepte des requêtes.</summary>
    public bool IsRunning => _listener?.IsListening ?? false;

    /// <summary>Adresse d'écoute.</summary>
    public string Prefix => _settings.Prefix;

    /// <summary>Démarre l'écoute. Ne lève pas : une API qui ne monte pas ne tue pas Optimus.</summary>
    public Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            return Task.FromResult(true);
        }

        try
        {
            HttpListener listener = new();
            listener.Prefixes.Add(_settings.Prefix);
            listener.Start();

            _listener = listener;
            _stopping = new CancellationTokenSource();
            _loop = Task.Run(() => AcceptAsync(_stopping.Token));

            _runtime.Activity += OnActivity;
            _runtime.StateChanged += OnStateChanged;

            DiagnosticLog.Info(
                "API locale démarrée",
                $"{_settings.Prefix} · {_tokens.Count} jeton(s) · "
                + $"{_settings.ExecutionsPerMinute} exécutions/min");

            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            // Port deja pris, pare-feu, politique d'entreprise : Optimus continue sans son API.
            DiagnosticLog.Warn(
                $"API locale indisponible sur {_settings.Prefix}", exception.Message);

            return Task.FromResult(false);
        }
    }

    public async Task StopAsync()
    {
        if (_listener is null)
        {
            return;
        }

        _runtime.Activity -= OnActivity;
        _runtime.StateChanged -= OnStateChanged;

        try
        {
            _stopping?.Cancel();
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // Un serveur qu'on arrete n'a plus rien a promettre.
        }

        _listener = null;

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // La boucle se termine par une exception quand l'ecoute est coupee : normal.
            }

            _loop = null;
        }

        await CloseSocketsAsync().ConfigureAwait(false);

        DiagnosticLog.Info("API locale arrêtée", null);
    }

    // ------------------------------------------------------------------------ la boucle

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is { IsListening: true })
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // L'ecoute a ete coupee : c'est la sortie normale de cette boucle.
                return;
            }

            // Chaque requete sur son propre fil : une synthese vocale ou une macro longue ne doit
            // pas bloquer les suivantes, ni le flux d'evenements.
            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            ApiToken? token = Authenticate(context.Request);

            if (token is null)
            {
                await RefuseAsync(context, HttpStatusCode.Unauthorized, "Jeton absent ou invalide.")
                    .ConfigureAwait(false);

                return;
            }

            string path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            string method = context.Request.HttpMethod.ToUpperInvariant();

            if (context.Request.IsWebSocketRequest)
            {
                await EventsAsync(context, token, path).ConfigureAwait(false);
                return;
            }

            await RouteAsync(context, token, method, path).ConfigureAwait(false);
        }
        catch (ApiRequestException refusal)
        {
            await RefuseAsync(context, HttpStatusCode.BadRequest, refusal.Message)
                .ConfigureAwait(false);
        }
        catch (JsonException malformed)
        {
            await RefuseAsync(context, HttpStatusCode.BadRequest, $"JSON illisible : {malformed.Message}")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DiagnosticLog.Error("requête d'API en échec", exception);

            try
            {
                await RefuseAsync(context, HttpStatusCode.InternalServerError, exception.Message)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // La reponse est peut-etre deja partie : plus rien a dire au client.
            }
        }
    }

    // ------------------------------------------------------------------------ les routes

    private async Task RouteAsync(
        HttpListenerContext context, ApiToken token, string method, string path)
    {
        switch (method, path)
        {
            case ("GET", "/api/status"):
                await AnswerAsync(context, token, ApiScope.Read, Status).ConfigureAwait(false);
                return;

            case ("GET", "/api/commands"):
                await AnswerAsync(context, token, ApiScope.Read, Commands).ConfigureAwait(false);
                return;

            case ("POST", "/api/intents/resolve"):
                await AnswerAsync(context, token, ApiScope.Read, ResolveAsync).ConfigureAwait(false);
                return;

            case ("POST", "/api/utterance"):
                await AnswerAsync(context, token, ApiScope.Execute, UtteranceAsync).ConfigureAwait(false);
                return;

            case ("POST", "/api/say"):
                await AnswerAsync(context, token, ApiScope.Write, SayAsync).ConfigureAwait(false);
                return;

            case ("POST", "/api/system/killswitch"):
                await AnswerAsync(context, token, ApiScope.Write, KillSwitchAsync).ConfigureAwait(false);
                return;

            case ("POST", "/api/system/simulation"):
                await AnswerAsync(context, token, ApiScope.Write, SimulationAsync).ConfigureAwait(false);
                return;
        }

        if (method == "POST" && path.StartsWith("/api/commands/", StringComparison.Ordinal)
            && path.EndsWith("/execute", StringComparison.Ordinal))
        {
            string id = path["/api/commands/".Length..^"/execute".Length];

            await AnswerAsync(context, token, ApiScope.Execute, body => ExecuteAsync(id, body))
                .ConfigureAwait(false);

            return;
        }

        await RefuseAsync(context, HttpStatusCode.NotFound, $"Aucune route « {method} {path} ».")
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Vérifie la portée, lit le corps, écrit la réponse.
    ///
    /// Le contrôle de portée est ici et non dans chaque gestionnaire : un point unique ne peut
    /// pas être oublié dans une route ajoutée plus tard, ce qui est exactement ainsi qu'une
    /// route se retrouve ouverte sans que personne ne l'ait voulu.
    /// </summary>
    private async Task AnswerAsync(
        HttpListenerContext context,
        ApiToken token,
        ApiScope required,
        Func<JsonElement, Task<object>> handler)
    {
        if (!token.Scopes.HasFlag(required))
        {
            await RefuseAsync(
                context,
                HttpStatusCode.Forbidden,
                $"Le jeton « {token.Name} » n'a pas la portée « {required} ».").ConfigureAwait(false);

            return;
        }

        if (required == ApiScope.Execute && !_limiter.Allow(token.Name))
        {
            await RefuseAsync(
                context,
                HttpStatusCode.TooManyRequests,
                $"Plafond atteint : {_settings.ExecutionsPerMinute} exécutions par minute.")
                .ConfigureAwait(false);

            return;
        }

        JsonElement body = await ReadBodyAsync(context.Request).ConfigureAwait(false);
        object payload = await handler(body).ConfigureAwait(false);

        await WriteAsync(context, HttpStatusCode.OK, payload).ConfigureAwait(false);
    }

    private Task<object> Status(JsonElement _)
    {
        GameStatus game = _runtime.Detector.Detect();

        return Task.FromResult<object>(new
        {
            copilot = _runtime.Copilot.Name,
            voice = _runtime.Copilot.Voice.VoiceId,
            speech = _runtime.Speech.Id,
            listening = _runtime.IsListening,
            simulation = _runtime.SimulationMode,
            kill_switch = _runtime.KillSwitch,
            combat = _runtime.State.CombatActive,
            binding_profile = _runtime.BindingProfileName,
            commands = _runtime.Catalog.Count,
            bound_actions = _runtime.Bindings.BoundCount,
            conversation = _runtime.HasConversation,
            game = new
            {
                running = game.IsRunning,
                foreground = game.IsForeground,
            },
        });
    }

    private Task<object> Commands(JsonElement _) =>
        Task.FromResult<object>(new
        {
            catalog = _runtime.Catalog.Id,
            count = _runtime.Catalog.Count,
            commands = _runtime.Catalog.Commands.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                category = c.Category,
                kind = c.Kind.ToString().ToLowerInvariant(),
                dangerous = c.Dangerous,
                has_polarity = c.HasPolarity,
                phrases = c.AllPhrases.ToArray(),
            }).ToArray(),
        });

    /// <summary>
    /// Résout un énoncé <b>sans rien exécuter</b>.
    ///
    /// C'est la route qui rend l'API sûre à explorer : un client peut éprouver une formulation,
    /// voir ce qu'Optimus en aurait fait et ce qui arrivait second, sans qu'une seule touche ne
    /// parte. Elle n'exige donc que la portée de lecture.
    /// </summary>
    private Task<object> ResolveAsync(JsonElement body)
    {
        string text = Text(body, "text");

        IntentResolution resolution = new FastIntentMatcher(_runtime.Catalog)
            .Resolve(text, _runtime.Copilot.WakeWord);

        return Task.FromResult<object>(new
        {
            text,
            normalized = resolution.NormalizedText,
            decision = resolution.Decision.ToString().ToLowerInvariant(),
            command = resolution.Best?.Command.Id,
            name = resolution.Best?.Command.Name,
            score = Math.Round(resolution.Best?.Score ?? 0, 3),
            polarity = (resolution.Best?.Polarity ?? CommandPolarity.Neutral)
                .ToString().ToLowerInvariant(),
            candidates = resolution.Candidates.Take(3).Select(c => new
            {
                command = c.Command.Id,
                score = Math.Round(c.Score, 3),
                kind = c.Kind.ToString().ToLowerInvariant(),
            }).ToArray(),
        });
    }

    private async Task<object> UtteranceAsync(JsonElement body)
    {
        string text = Text(body, "text");

        await _runtime.HandleUtteranceAsync(text).ConfigureAwait(false);

        return new { accepted = true, text };
    }

    private async Task<object> ExecuteAsync(string id, JsonElement body)
    {
        if (!_runtime.Catalog.TryGet(id, out CommandDefinition? command) || command is null)
        {
            throw new ApiRequestException($"Aucune commande « {id} » au catalogue.");
        }

        CommandPolarity polarity = Text(body, "polarity", required: false).ToLowerInvariant() switch
        {
            "on" => CommandPolarity.On,
            "off" => CommandPolarity.Off,
            _ => CommandPolarity.Neutral,
        };

        ExecutionResult result = await _runtime.RunCommandAsync(command, polarity)
            .ConfigureAwait(false);

        return new
        {
            trace = result.TraceId,
            status = result.Status.ToString().ToLowerInvariant(),
            command = command.Id,
            polarity = polarity.ToString().ToLowerInvariant(),
            message = result.Message,
            elapsed_ms = Math.Round(result.TotalMs, 1),
        };
    }

    private async Task<object> SayAsync(JsonElement body)
    {
        string text = Text(body, "text");

        await _runtime.Speech.SpeakAsync(new Core.Abstractions.SpeechRequest(
            text,
            _runtime.Copilot.Voice.VoiceId,
            _runtime.Copilot.EffectiveRate,
            _runtime.Copilot.Voice.Volume)).ConfigureAwait(false);

        return new { spoken = text };
    }

    private Task<object> KillSwitchAsync(JsonElement body)
    {
        bool engaged = Flag(body, "engaged");

        _runtime.SetKillSwitch(engaged);

        return Task.FromResult<object>(new { kill_switch = _runtime.KillSwitch });
    }

    private Task<object> SimulationAsync(JsonElement body)
    {
        _runtime.SetSimulation(Flag(body, "simulation"));

        return Task.FromResult<object>(new { simulation = _runtime.SimulationMode });
    }

    // ------------------------------------------------------------------- le flux d'evenements

    private async Task EventsAsync(HttpListenerContext context, ApiToken token, string path)
    {
        if (path != "/ws/events" || !token.Scopes.HasFlag(ApiScope.Read))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            context.Response.Close();
            return;
        }

        HttpListenerWebSocketContext socket =
            await context.AcceptWebSocketAsync(null).ConfigureAwait(false);

        await _socketLock.WaitAsync().ConfigureAwait(false);

        try
        {
            _sockets.Add(socket.WebSocket);
        }
        finally
        {
            _socketLock.Release();
        }

        DiagnosticLog.Debug($"flux d'événements ouvert · {token.Name}", null);

        // On ne lit rien du client : ce flux ne sert qu'a pousser. Lire quand meme est ce qui
        // permet de detecter sa fermeture, sans quoi la liste enflerait de sockets mortes.
        byte[] scratch = new byte[256];

        try
        {
            while (socket.WebSocket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult received = await socket.WebSocket
                    .ReceiveAsync(scratch, CancellationToken.None)
                    .ConfigureAwait(false);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (Exception)
        {
            // Client parti sans ceremonie : rien d'anormal.
        }
        finally
        {
            await ForgetAsync(socket.WebSocket).ConfigureAwait(false);
        }
    }

    private void OnActivity(object? sender, SessionActivity activity) =>
        Broadcast(new
        {
            type = "activity",
            heard = activity.Recognition?.Text,
            confidence = activity.Recognition?.Confidence,
            command = activity.Result?.Command?.Id,
            status = activity.Result?.Status.ToString().ToLowerInvariant(),
            spoken = activity.Spoken,
        });

    private void OnStateChanged(object? sender, EventArgs e) =>
        Broadcast(new
        {
            type = "state",
            listening = _runtime.IsListening,
            simulation = _runtime.SimulationMode,
            kill_switch = _runtime.KillSwitch,
            binding_profile = _runtime.BindingProfileName,
        });

    private void Broadcast(object payload)
    {
        // Detache : ces evenements viennent du fil du moteur vocal, et une socket lente ne doit
        // pas retarder une commande que le pilote attend.
        _ = Task.Run(async () =>
        {
            byte[] frame;

            try
            {
                frame = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
            }
            catch (Exception exception)
            {
                // Une serialisation qui echoue dans un Task.Run detache disparait sans laisser
                // de trace : le flux se tairait, et rien n'expliquerait pourquoi.
                DiagnosticLog.Warn("événement d'API non sérialisable", exception.Message);
                return;
            }

            List<WebSocket> targets;

            await _socketLock.WaitAsync().ConfigureAwait(false);

            try
            {
                targets = [.. _sockets];
            }
            finally
            {
                _socketLock.Release();
            }

            DiagnosticLog.Debug($"diffusion vers {targets.Count} flux", $"{frame.Length} octets");

            foreach (WebSocket socket in targets)
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.SendAsync(
                            frame, WebSocketMessageType.Text, true, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    DiagnosticLog.Debug("flux d'événements rompu", exception.Message);
                    await ForgetAsync(socket).ConfigureAwait(false);
                }
            }
        });
    }

    private async Task ForgetAsync(WebSocket socket)
    {
        await _socketLock.WaitAsync().ConfigureAwait(false);

        try
        {
            _sockets.Remove(socket);
        }
        finally
        {
            _socketLock.Release();
        }

        socket.Dispose();
    }

    private async Task CloseSocketsAsync()
    {
        List<WebSocket> targets;

        await _socketLock.WaitAsync().ConfigureAwait(false);

        try
        {
            targets = [.. _sockets];
            _sockets.Clear();
        }
        finally
        {
            _socketLock.Release();
        }

        foreach (WebSocket socket in targets)
        {
            try
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "Optimus s'arrête", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Deja tombee.
            }

            socket.Dispose();
        }
    }

    // ------------------------------------------------------------------------ le plomberie

    private ApiToken? Authenticate(HttpListenerRequest request)
    {
        string? header = request.Headers["Authorization"];

        if (string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string candidate = header["Bearer ".Length..].Trim();

        return _tokens.FirstOrDefault(t => t.Matches(candidate));
    }

    private static async Task<JsonElement> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return default;
        }

        using StreamReader reader = new(request.InputStream, request.ContentEncoding);
        string raw = await reader.ReadToEndAsync().ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        using JsonDocument document = JsonDocument.Parse(raw);

        return document.RootElement.Clone();
    }

    private static string Text(JsonElement body, string property, bool required = true)
    {
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return required
            ? throw new ApiRequestException($"Le champ « {property} » est attendu.")
            : string.Empty;
    }

    private static bool Flag(JsonElement body, string property) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private static Task WriteAsync(HttpListenerContext context, HttpStatusCode status, object payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;

        return WriteBodyAsync(context, bytes);
    }

    private static async Task WriteBodyAsync(HttpListenerContext context, byte[] bytes)
    {
        try
        {
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static Task RefuseAsync(HttpListenerContext context, HttpStatusCode status, string reason)
    {
        // Le motif est dit en clair : une API muette sur ses refus se debogue a l'aveugle, et
        // celle-ci n'ecoute que la machine du pilote.
        if (status == HttpStatusCode.Unauthorized)
        {
            context.Response.AddHeader("WWW-Authenticate", "Bearer");
        }

        return WriteAsync(context, status, new { error = reason });
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        _socketLock.Dispose();
    }
}
