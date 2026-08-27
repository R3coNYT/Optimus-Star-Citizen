using System.Collections.ObjectModel;
using System.Windows;
using Optimus.App.Input;
using Optimus.App.Mvvm;
using Optimus.Core.Abstractions;
using Optimus.Core.Ai;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Domain.Profiles;
using Optimus.Core.Loading;
using Optimus.Core.Personality;
using Optimus.Infrastructure.Hosting;
using Optimus.Infrastructure.Input;
using Optimus.Infrastructure.Speech;

namespace Optimus.App.ViewModels;

/// <summary>
/// Les réglages, tels qu'on peut les changer sans ouvrir un fichier.
///
/// Rien n'est appliqué avant « Enregistrer » : on doit pouvoir déplacer un curseur, en écouter
/// l'effet, puis renoncer. L'écriture patche les fichiers — elle ne les régénère pas — et le
/// moteur les relit ensuite, de sorte que ce qui s'affiche et ce qui sera chargé au prochain
/// démarrage ne peuvent pas différer.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly OptimusRuntime _runtime;
    private readonly Action<string, string?, ActivityLevel> _log;

    private bool _pushToTalk;
    private string _pushToTalkKey = "INSERT";
    private bool _requireWakeWordInPushToTalk;
    private string _wakeWord = "Optimus";
    private double _confidenceThreshold;
    private double _noiseFloor;
    private string? _voiceId;
    private double _rate;
    private double _volume;
    private int _humor;
    private int _sarcasm;
    private int _formality;
    private int _verbosity;
    private int _calmness;
    private int _warmth;
    private bool _aiEnabled;
    private string _aiProvider = "ollama";
    private string _aiEndpoint = string.Empty;
    private string _aiModel = string.Empty;
    private int _aiBudget = 200;
    private string _aiProbe = string.Empty;
    private bool _neuralVoice;
    private bool _dirty;

    public SettingsViewModel(OptimusRuntime runtime, Action<string, string?, ActivityLevel> log)
    {
        _runtime = runtime;
        _log = log;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsDirty);
        RevertCommand = new RelayCommand(Revert, () => IsDirty);
        CaptureKeyCommand = new AsyncRelayCommand(CaptureKeyAsync, () => PushToTalk);
        TestVoiceCommand = new AsyncRelayCommand(TestVoiceAsync);
        ProbeAiCommand = new AsyncRelayCommand(ProbeAiAsync, () => AiEnabled);

        Revert();
    }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand RevertCommand { get; }

    public AsyncRelayCommand CaptureKeyCommand { get; }

    public AsyncRelayCommand TestVoiceCommand { get; }

    /// <summary>Vérifie que le fournisseur répond, sans consommer de jetons.</summary>
    public AsyncRelayCommand ProbeAiCommand { get; }

    public ObservableCollection<string> Voices { get; } = new();

    /// <summary>Fenêtre propriétaire, requise pour capturer une touche.</summary>
    public Window? Owner { get; set; }

    public bool IsDirty
    {
        get => _dirty;
        private set
        {
            if (Set(ref _dirty, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool PushToTalk
    {
        get => _pushToTalk;
        set
        {
            if (Track(ref _pushToTalk, value))
            {
                Raise(nameof(AlwaysOn));
                Raise(nameof(ModeExplanation));
                CaptureKeyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Inverse de <see cref="PushToTalk"/>, pour lier deux boutons radio.</summary>
    public bool AlwaysOn
    {
        get => !_pushToTalk;
        set
        {
            if (value)
            {
                PushToTalk = false;
            }
        }
    }

    /// <summary>
    /// Ce que le mode choisi implique vraiment.
    ///
    /// Le compromis n'est pas évident : l'écoute permanente n'est sûre que parce que la
    /// grammaire exige le mot d'éveil en tête (D30). Le dire ici évite de le redécouvrir.
    /// </summary>
    public string ModeExplanation => PushToTalk
        ? "La touche délimite la commande. Aucun faux déclenchement possible, au prix d'un doigt occupé. "
          + "Le micro reste ouvert en permanence — l'ouvrir à l'appui coûterait 419 ms et tronquerait "
          + "le début de chaque phrase."
        : "La grammaire n'accepte que les phrases commençant par le mot d'éveil : une conversation "
          + "ordinaire ne correspond à aucune alternative et se trouve rejetée sans même avoir été "
          + "transcrite. Rien de ce que vous dites d'autre n'est analysé.";

    public string PushToTalkKey
    {
        get => _pushToTalkKey;
        set => Track(ref _pushToTalkKey, value);
    }

    public bool RequireWakeWordInPushToTalk
    {
        get => _requireWakeWordInPushToTalk;
        set => Track(ref _requireWakeWordInPushToTalk, value);
    }

    public string WakeWord
    {
        get => _wakeWord;
        set => Track(ref _wakeWord, value);
    }

    public double ConfidenceThreshold
    {
        get => _confidenceThreshold;
        set
        {
            if (Track(ref _confidenceThreshold, Math.Round(value, 2)))
            {
                Raise(nameof(ThresholdExplanation));
            }
        }
    }

    public double NoiseFloor
    {
        get => _noiseFloor;
        set
        {
            if (Track(ref _noiseFloor, Math.Round(value, 2)))
            {
                Raise(nameof(ThresholdExplanation));
            }
        }
    }

    /// <summary>
    /// Ce que les deux seuils délimitent.
    ///
    /// Trois bandes, et la bande du milieu est la raison d'être du réglage : les confiances des
    /// vraies commandes et des phrases hors catalogue se chevauchent, aucun seuil unique ne peut
    /// donc les séparer (D29).
    /// </summary>
    public string ThresholdExplanation =>
        $"Sous {NoiseFloor:F2} : ignoré sans un mot.  "
        + $"De {NoiseFloor:F2} à {ConfidenceThreshold:F2} : Optimus propose et attend « confirme ».  "
        + $"Au-dessus de {ConfidenceThreshold:F2} : exécuté.";

    public string? VoiceId
    {
        get => _voiceId;
        set => Track(ref _voiceId, value);
    }

    public double Rate
    {
        get => _rate;
        set => Track(ref _rate, Math.Round(value, 2));
    }

    public double Volume
    {
        get => _volume;
        set => Track(ref _volume, Math.Round(value, 2));
    }

    /// <summary>Le copilote parle-t-il avec une voix neuronale locale plutôt qu'une voix Windows ?</summary>
    public bool NeuralVoice
    {
        get => _neuralVoice;
        set
        {
            if (Track(ref _neuralVoice, value))
            {
                Raise(nameof(VoiceEngineExplanation));
            }
        }
    }

    /// <summary>Vrai si Piper est installé et utilisable.</summary>
    public bool NeuralVoiceAvailable => PiperInstallation.Locate() is not null;

    /// <summary>
    /// Le compromis, dit en clair plutôt que découvert à l'usage.
    ///
    /// Les chiffres sont mesurés sur cette machine le 2026-08-27, pas repris d'une brochure :
    /// une voix neuronale coûte une fraction de seconde de plus <b>par réplique</b>. C'est
    /// supportable parce que la parole vient après l'action — le vaisseau a déjà obéi quand
    /// Optimus commente — mais personne ne devrait s'en apercevoir en vol.
    /// </summary>
    public string VoiceEngineExplanation => !NeuralVoiceAvailable
        ? "Piper n'est pas installé. Placez piper.exe et au moins un modèle .onnx dans "
          + $"{PiperInstallation.DefaultRoot} — le binaire vient des versions publiées de "
          + "rhasspy/piper, les voix de huggingface.co/rhasspy/piper-voices."
        : NeuralVoice
            ? "Le modèle tourne sur cette machine : rien ne part sur le réseau. Compter environ "
              + "400 ms par réplique avec une voix « medium », contre 10 ms pour une voix "
              + "Windows. La parole venant après l'action, ce délai porte sur le commentaire, "
              + "jamais sur la commande. Une voix « low » divise l'attente par deux."
            : "Voix Windows : quasi instantanées (7 à 15 ms), toujours disponibles, mais au "
              + "timbre synthétique. C'est le choix sûr.";

    public bool AiEnabled
    {
        get => _aiEnabled;
        set
        {
            if (Track(ref _aiEnabled, value))
            {
                ProbeAiCommand.RaiseCanExecuteChanged();
                Raise(nameof(AiExplanation));
            }
        }
    }

    /// <summary>
    /// Ce que l'activation implique vraiment.
    ///
    /// Le dire ici plutôt que dans une documentation que personne n'ouvre : la différence entre
    /// un modèle local et un service distant n'est pas une préférence technique, c'est la
    /// question de savoir si ce qu'on dit à son copilote quitte la machine.
    /// </summary>
    public string AiExplanation => !AiEnabled
        ? "Désactivé. Optimus fonctionne entièrement hors ligne : le catalogue et la grammaire "
          + "suffisent, et rien ne part sur le réseau."
        : AiProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? "Le modèle tourne sur cette machine. Ce que vous dites ne la quitte pas. "
              + "N'intervient qu'après l'échec du chemin rapide, et ne peut désigner qu'une "
              + "commande du catalogue — jamais une touche."
            : "Service distant : les énoncés qu'Optimus n'a pas compris lui seront envoyés. "
              + "La clé éventuelle passe par la variable d'environnement OPTIMUS_AI_KEY, "
              + "jamais par un fichier de configuration.";

    public string AiProvider
    {
        get => _aiProvider;
        set
        {
            if (Track(ref _aiProvider, value))
            {
                Raise(nameof(AiExplanation));
            }
        }
    }

    public string AiEndpoint
    {
        get => _aiEndpoint;
        set => Track(ref _aiEndpoint, value);
    }

    public string AiModel
    {
        get => _aiModel;
        set => Track(ref _aiModel, value);
    }

    public int AiBudget
    {
        get => _aiBudget;
        set => Track(ref _aiBudget, value);
    }

    /// <summary>Résultat du dernier essai de connexion.</summary>
    public string AiProbe
    {
        get => _aiProbe;
        private set => Set(ref _aiProbe, value);
    }

    public int Humor
    {
        get => _humor;
        set => TrackTrait(ref _humor, value);
    }

    public int Sarcasm
    {
        get => _sarcasm;
        set => TrackTrait(ref _sarcasm, value);
    }

    public int Formality
    {
        get => _formality;
        set => TrackTrait(ref _formality, value);
    }

    public int Verbosity
    {
        get => _verbosity;
        set => TrackTrait(ref _verbosity, value);
    }

    public int Calmness
    {
        get => _calmness;
        set => TrackTrait(ref _calmness, value);
    }

    public int Warmth
    {
        get => _warmth;
        set => TrackTrait(ref _warmth, value);
    }

    /// <summary>Budget de mots dérivé de la verbosité, montré parce que c'est l'effet le plus visible.</summary>
    public string VerbosityEffect =>
        $"{new PersonalityTraits(Verbosity: Verbosity).MaxWords} mots au maximum par réplique";

    /// <summary>
    /// Trois répliques telles que ces curseurs les produiraient, tirées du vrai catalogue.
    ///
    /// Un aperçu vaut mieux qu'une explication : déplacer « sarcasme » fait apparaître ou
    /// disparaître des variantes, et on le voit immédiatement au lieu de le deviner.
    /// </summary>
    public string Preview
    {
        get
        {
            PersonalityTraits traits = CurrentTraits();
            Core.Domain.Personality.Personality personality = _runtime.Copilot.Personality with { Traits = traits };

            // Graine fixe : l'apercu doit changer quand les CURSEURS changent, pas a chaque
            // redessin de la fenetre.
            ResponseComposer composer = new(personality, _runtime.Copilot.Responses, seed: 7);

            string[] samples =
            [
                Compose(composer, "ship.lights.on", ResponseEvent.Success),
                Compose(composer, "system.success", ResponseEvent.Success),
                Compose(composer, "system.no_binding", ResponseEvent.Fail),
            ];

            return string.Join("\n", samples.Where(s => s.Length > 0).Select(s => $"« {s} »"));
        }
    }

    /// <summary>Charge la liste des voix installées.</summary>
    public async Task LoadVoicesAsync()
    {
        List<VoiceInfo> voices = new(
            await _runtime.Speech.GetVoicesAsync().ConfigureAwait(true));

        // Les voix Piper meme quand le moteur actif est celui de Windows : sans cela, choisir une
        // voix neuronale demanderait d'enregistrer, de redemarrer, puis de revenir choisir — trois
        // gestes pour un seul reglage.
        if (PiperInstallation.Locate() is PiperInstallation installation
            && !_runtime.Speech.Id.Equals(SpeechFactory.Piper, StringComparison.OrdinalIgnoreCase))
        {
            voices.InsertRange(0, installation.Voices());
        }

        Voices.Clear();

        foreach (VoiceInfo voice in voices)
        {
            Voices.Add(voice.DisplayName);
        }

        // La voix configurée peut avoir disparu — une mise à jour de Windows, un autre poste.
        // Mieux vaut la montrer absente que la remplacer en douce par une autre.
        if (VoiceId is not null && !Voices.Contains(VoiceId))
        {
            Voices.Insert(0, VoiceId);
        }

        Raise(nameof(Voices));
    }

    private void Revert()
    {
        VoiceInputSettings input = _runtime.User.VoiceInput;
        VoiceConfig voice = _runtime.Copilot.Voice;
        PersonalityTraits traits = _runtime.Copilot.Personality.Traits;

        _pushToTalk = input.Mode == ListeningMode.PushToTalk;
        _pushToTalkKey = input.PushToTalkKey;
        _requireWakeWordInPushToTalk = input.RequireWakeWordInPushToTalk;
        _confidenceThreshold = input.ConfidenceThreshold;
        _noiseFloor = input.NoiseFloor;

        _wakeWord = _runtime.Copilot.WakeWord;
        _voiceId = voice.VoiceId;
        _neuralVoice = string.Equals(voice.Provider, SpeechFactory.Piper, StringComparison.OrdinalIgnoreCase);
        _rate = voice.Rate;
        _volume = voice.Volume;

        AiSettings ai = _runtime.User.Ai ?? AiSettings.Disabled;
        _aiEnabled = ai.Enabled;
        _aiProvider = ai.Provider;
        _aiEndpoint = ai.Endpoint;
        _aiModel = ai.Model;
        _aiBudget = ai.CallBudget;
        _aiProbe = string.Empty;

        _humor = traits.Humor;
        _sarcasm = traits.Sarcasm;
        _formality = traits.Formality;
        _verbosity = traits.Verbosity;
        _calmness = traits.Calmness;
        _warmth = traits.Warmth;

        IsDirty = false;
        RaiseAll();
    }

    private async Task SaveAsync()
    {
        if (!PushToTalkWatcher.IsSupported(PushToTalkKey))
        {
            MessageBox.Show(
                $"« {PushToTalkKey} » ne peut pas servir de touche de push-to-talk.\n\n"
                + "Choisissez-en une autre avec le bouton de capture.",
                "Optimus", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(WakeWord))
        {
            MessageBox.Show(
                "Le mot d'éveil ne peut pas être vide : c'est lui qui distingue une commande "
                + "d'une conversation.",
                "Optimus", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SettingsWriter.SaveVoiceInput(_runtime.ProfilePath, new VoiceInputSettings(
            PushToTalk ? ListeningMode.PushToTalk : ListeningMode.AlwaysOn,
            PushToTalkKey,
            RequireWakeWordInPushToTalk,
            ConfidenceThreshold,
            NoiseFloor,
            _runtime.User.VoiceInput.InputDeviceId));

        SettingsWriter.SaveCopilotVoice(
            _runtime.CopilotPath,
            _runtime.Copilot.Voice with
            {
                Provider = NeuralVoice ? SpeechFactory.Piper : "windows-onecore",
                VoiceId = VoiceId,
                Rate = Rate,
                Volume = Volume,
            },
            WakeWord.Trim());

        SettingsWriter.SaveTraits(_runtime.PersonalityPath, CurrentTraits());

        SettingsWriter.SaveAi(_runtime.ProfilePath, new AiSettings(
            AiEnabled, AiProvider.Trim(), AiEndpoint.Trim(), AiModel.Trim(), Math.Max(1, AiBudget)));

        await _runtime.ReloadSettingsAsync().ConfigureAwait(true);

        IsDirty = false;
        RaiseAll();

        _log("réglages enregistrés",
            _runtime.IsListening ? "L'écoute a redémarré pour les prendre en compte." : null,
            ActivityLevel.Normal);
    }

    private async Task CaptureKeyAsync()
    {
        if (Owner is null)
        {
            return;
        }

        _log("pressez la touche de push-to-talk", "Échap pour renoncer", ActivityLevel.Speech);

        Core.Domain.Bindings.InputSpec? captured = await WindowKeyCapture
            .CaptureAsync(Owner, TimeSpan.FromSeconds(20))
            .ConfigureAwait(true);

        if (captured is null)
        {
            return;
        }

        if (!PushToTalkWatcher.IsSupported(captured.Key))
        {
            _log($"« {captured.Key} » ne convient pas comme touche de push-to-talk",
                "Choisissez une touche ordinaire, une touche de fonction, ou Inser / Suppr.",
                ActivityLevel.Warning);
            return;
        }

        PushToTalkKey = captured.Key;
    }

    private async Task TestVoiceAsync()
    {
        // On teste ce qui est A L'ECRAN, pas ce qui est enregistre : autrement, regler le debit
        // puis l'ecouter demanderait de sauvegarder d'abord, donc de valider a l'aveugle.
        await _runtime.Speech.SpeakAsync(new SpeechRequest(
            "Systèmes en ligne. À vos ordres, commandant.", VoiceId, Rate, Volume))
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Vérifie que le fournisseur répond, avant d'en dépendre.
    ///
    /// Une interrogation qui ne consomme rien : mieux vaut découvrir maintenant qu'Ollama n'est
    /// pas lancé qu'en plein vol, quand Optimus se contentera de ne pas comprendre.
    /// </summary>
    private async Task ProbeAiAsync()
    {
        AiProbe = "Essai en cours…";

        AiSettings settings = new(
            true, AiProvider.Trim(), AiEndpoint.Trim(), AiModel.Trim(), Math.Max(1, AiBudget));

        await using Optimus.Infrastructure.Ai.HttpLanguageModel model = new(settings);

        bool reachable = await model.IsReachableAsync().ConfigureAwait(true);

        AiProbe = reachable
            ? $"Le fournisseur répond. Vérifiez que « {settings.Model} » y est bien installé."
            : $"Aucune réponse de {settings.Endpoint}. Le service est-il lancé ?";
    }

    private PersonalityTraits CurrentTraits() =>
        _runtime.Copilot.Personality.Traits with
        {
            Humor = Humor,
            Sarcasm = Sarcasm,
            Formality = Formality,
            Verbosity = Verbosity,
            Calmness = Calmness,
            Warmth = Warmth,
        };

    private static string Compose(ResponseComposer composer, string key, ResponseEvent responseEvent) =>
        composer.Compose(key, responseEvent,
            new Dictionary<string, string> { ["command"] = "Feux du vaisseau" })?.Text ?? string.Empty;

    private bool Track<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (!Set(ref field, value, property))
        {
            return false;
        }

        IsDirty = true;
        return true;
    }

    private void TrackTrait(ref int field, int value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (Track(ref field, value, property))
        {
            Raise(nameof(Preview));
            Raise(nameof(VerbosityEffect));
        }
    }

    private void RaiseAll()
    {
        foreach (string property in new[]
        {
            nameof(PushToTalk), nameof(AlwaysOn), nameof(ModeExplanation), nameof(PushToTalkKey),
            nameof(RequireWakeWordInPushToTalk), nameof(WakeWord), nameof(ConfidenceThreshold),
            nameof(NoiseFloor), nameof(ThresholdExplanation), nameof(VoiceId), nameof(Rate),
            nameof(Volume), nameof(Humor), nameof(Sarcasm), nameof(Formality), nameof(Verbosity),
            nameof(Calmness), nameof(Warmth), nameof(Preview), nameof(VerbosityEffect),
            nameof(NeuralVoice), nameof(NeuralVoiceAvailable), nameof(VoiceEngineExplanation),
            nameof(AiEnabled), nameof(AiProvider), nameof(AiEndpoint), nameof(AiModel),
            nameof(AiBudget), nameof(AiExplanation), nameof(AiProbe),
        })
        {
            Raise(property);
        }
    }
}
