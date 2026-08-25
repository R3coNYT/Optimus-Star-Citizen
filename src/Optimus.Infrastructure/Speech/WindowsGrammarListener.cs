using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Recognition;
using Optimus.Core.Abstractions;
using Optimus.Core.Intent;

namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Écoute du microphone par le moteur de reconnaissance de Windows, contraint par une grammaire.
///
/// Mesuré au spike S0-6 sur de vrais enregistrements : <b>16,7 ms de latence médiane et 21
/// commandes justes sur 21</b>, là où Whisper demandait 3 336 ms avec le jeu lancé. L'écart ne
/// tient pas à une optimisation mais à la nature de l'outil : ce moteur ne transcrit pas, il
/// choisit parmi les phrases qu'on l'autorise à entendre.
///
/// Corollaire précieux : ce qui ne figure pas dans la grammaire n'est pas reconnu — donc une
/// conversation ordinaire, en écoute permanente, ne déclenche rien.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGrammarListener : IVoiceCommandListener
{
    private readonly SpeechRecognitionEngine _engine;
    private readonly VoiceGrammar _grammar;
    private readonly Grammar _loadedGrammar;
    private readonly double _confidenceThreshold;
    private bool _listening;
    private bool _active = true;
    private bool _disposed;

    /// <param name="grammar">Grammaire assemblée depuis le catalogue.</param>
    /// <param name="confidenceThreshold">Seuil d'acceptation. 0,40 mesuré au spike S0-6 (D29).</param>
    /// <param name="culture">Langue du moteur.</param>
    public WindowsGrammarListener(VoiceGrammar grammar, double confidenceThreshold = 0.40, string culture = "fr-FR")
    {
        ArgumentNullException.ThrowIfNull(grammar);

        if (grammar.Count == 0)
        {
            throw new ArgumentException("La grammaire est vide : le moteur n'aurait rien à reconnaître.", nameof(grammar));
        }

        _grammar = grammar;
        _confidenceThreshold = confidenceThreshold;

        RecognizerInfo recognizer = FindRecognizer(culture)
            ?? throw new InvalidOperationException(
                $"Aucun moteur de reconnaissance pour « {culture} ». " +
                "Ajoute le module vocal dans Paramètres > Heure et langue > Langue.");

        _engine = new SpeechRecognitionEngine(recognizer);

        Choices choices = new();
        choices.Add(grammar.Alternatives.ToArray());

        System.Speech.Recognition.GrammarBuilder builder = new()
        {
            Culture = CultureInfo.GetCultureInfo(culture),
        };
        builder.Append(choices);

        _loadedGrammar = new Grammar(builder) { Name = "optimus", Enabled = true };
        _engine.LoadGrammar(_loadedGrammar);

        _engine.SpeechRecognized += OnRecognized;
        _engine.SpeechRecognitionRejected += OnRejected;
    }

    public string Id => "windows-grammar";

    public bool IsListening => _listening;

    /// <summary>Moteur retenu, pour l'affichage.</summary>
    public string RecognizerName => _engine.RecognizerInfo.Name;

    /// <summary>Nombre d'alternatives chargées.</summary>
    public int GrammarSize => _grammar.Count;

    public event EventHandler<VoiceRecognition>? Recognized;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listening)
        {
            return Task.CompletedTask;
        }

        _engine.SetInputToDefaultAudioDevice();

        // RecognizeMode.Multiple : le moteur reste a l'ecoute apres chaque reconnaissance,
        // au lieu de s'arreter au premier resultat.
        _engine.RecognizeAsync(RecognizeMode.Multiple);
        _listening = true;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_listening)
        {
            return Task.CompletedTask;
        }

        _engine.RecognizeAsyncCancel();
        _listening = false;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Active ou suspend la grammaire.
    ///
    /// Le périphérique reste ouvert : c'est délibéré, rouvrir coûte 419 ms et tronquerait le
    /// début de la phrase suivante. En contrepartie, en push-to-talk, le microphone demeure
    /// techniquement capté par le moteur même hors appui — il n'y a simplement plus rien à
    /// reconnaître. Une fermeture réelle du périphérique demanderait de piloter nous-mêmes la
    /// capture ; c'est une évolution possible, pas une nécessité aujourd'hui.
    /// </summary>
    public void SetActive(bool active)
    {
        _active = active;

        if (!_disposed)
        {
            _loadedGrammar.Enabled = active;
        }
    }

    private void OnRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (!_active || e.Result is null)
        {
            return;
        }

        string text = e.Result.Text;
        double confidence = e.Result.Confidence;
        string? commandId = _grammar.Resolve(text);

        Recognized?.Invoke(this, new VoiceRecognition(
            text,
            Math.Round(confidence, 3),
            commandId,
            Accepted: confidence >= _confidenceThreshold && commandId is not null,
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Le moteur a entendu de la parole sans y reconnaître de commande.
    ///
    /// C'est le cas normal en écoute permanente — une conversation, une exclamation — et il est
    /// remonté malgré tout : c'est en observant ces rejets qu'on calibrera le seuil, et c'est
    /// ainsi que le mode debug pourra expliquer « je t'ai entendu mais je n'ai pas compris ».
    /// </summary>
    private void OnRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        if (!_active)
        {
            return;
        }

        string text = e.Result?.Text ?? string.Empty;

        Recognized?.Invoke(this, new VoiceRecognition(
            text,
            Math.Round(e.Result?.Confidence ?? 0, 3),
            CommandId: null,
            Accepted: false,
            DateTimeOffset.UtcNow));
    }

    /// <summary>Moteurs de reconnaissance installés, toutes langues confondues.</summary>
    public static IReadOnlyList<string> InstalledRecognizers() =>
        SpeechRecognitionEngine.InstalledRecognizers()
            .Select(r => $"{r.Name} ({r.Culture.Name})")
            .ToList();

    private static RecognizerInfo? FindRecognizer(string culture)
    {
        List<RecognizerInfo> installed = SpeechRecognitionEngine.InstalledRecognizers().ToList();

        return installed.FirstOrDefault(r =>
                   string.Equals(r.Culture.Name, culture, StringComparison.OrdinalIgnoreCase))
               ?? installed.FirstOrDefault(r =>
                   r.Culture.TwoLetterISOLanguageName.Equals(culture[..2], StringComparison.OrdinalIgnoreCase));
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        _engine.SpeechRecognized -= OnRecognized;
        _engine.SpeechRecognitionRejected -= OnRejected;

        try
        {
            _engine.RecognizeAsyncCancel();
        }
        catch (InvalidOperationException)
        {
            // Le moteur n'ecoutait pas : rien a annuler.
        }

        _engine.Dispose();
        return ValueTask.CompletedTask;
    }
}
