using System.Collections.ObjectModel;
using System.Windows;
using Optimus.App.Input;
using Optimus.App.Mvvm;
using Optimus.Core.Abstractions;
using Optimus.Core.Ai;
using Optimus.Core.Api;
using Optimus.Core.Speech;
using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Domain.Profiles;
using Optimus.Core.Loading;
using Optimus.Core.Localization;
using Optimus.Core.Personality;
using Optimus.Infrastructure.Hosting;
using Optimus.Infrastructure.Input;
using Optimus.Infrastructure.Api;
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
    private string? _copilot;
    private string _copilotName = string.Empty;
    private bool _switching;
    private WhisperMode _whisper = WhisperMode.Off;
    private bool _trimContext;
    private bool _apiEnabled;
    private int _apiPort = 8731;
    private int _apiRate = 30;
    private bool _dirty;
    private string? _language;

    public SettingsViewModel(OptimusRuntime runtime, Action<string, string?, ActivityLevel> log)
    {
        _runtime = runtime;
        _log = log;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsDirty);
        RevertCommand = new RelayCommand(Revert, () => IsDirty);
        CaptureKeyCommand = new AsyncRelayCommand(CaptureKeyAsync, () => PushToTalk);
        TestVoiceCommand = new AsyncRelayCommand(TestVoiceAsync);
        ProbeAiCommand = new AsyncRelayCommand(ProbeAiAsync, () => AiEnabled);
        RegenerateTokenCommand = new RelayCommand(RegenerateToken, () => ApiEnabled);
        DuplicateCopilotCommand = new AsyncRelayCommand(
            DuplicateCopilotAsync, () => !string.IsNullOrWhiteSpace(CopilotName));
        DeleteCopilotCommand = new AsyncRelayCommand(DeleteCopilotAsync, () => CanDeleteCopilot);

        // Meme raison que dans MainViewModel : les explications de cet ecran sont composees
        // en C#, et rien ne dirait a la vue de les relire.
        Localization.Localizer.Changed += () => Raise(null);

        Revert();
    }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand RevertCommand { get; }

    public AsyncRelayCommand CaptureKeyCommand { get; }

    public AsyncRelayCommand TestVoiceCommand { get; }

    /// <summary>Vérifie que le fournisseur répond, sans consommer de jetons.</summary>
    public AsyncRelayCommand ProbeAiCommand { get; }

    /// <summary>Émet un secret neuf. L'ancien cesse aussitôt de valoir.</summary>
    public RelayCommand RegenerateTokenCommand { get; }

    /// <summary>Crée un copilote à partir de celui qui est actif, puis lui passe la main.</summary>
    public AsyncRelayCommand DuplicateCopilotCommand { get; }

    public AsyncRelayCommand DeleteCopilotCommand { get; }

    /// <summary>Langues proposées, écrites chacune dans sa propre langue.</summary>
    public IReadOnlyList<string> Languages { get; } =
        Language.Known.Select(Language.DisplayName).ToList();

    /// <summary>
    /// Langue active. L'affecter bascule réellement, sans redémarrer.
    ///
    /// Comme le copilote, et pour la même raison : ce n'est pas un habillage. L'écran, les
    /// commandes qu'on prononce et les réponses changent d'un coup. Un aperçu à moitié
    /// appliqué — écran anglais, grammaire française — n'aurait aucun sens.
    /// </summary>
    public string? ActiveLanguage
    {
        get => _language;
        set
        {
            if (_switching || value is null || !Set(ref _language, value))
            {
                return;
            }

            _ = SwitchLanguageAsync(value);
        }
    }

    /// <summary>
    /// Ce qui manque à Windows pour entendre cette langue, ou <c>null</c> si rien ne manque.
    ///
    /// Dit <b>avant</b> de basculer, et non au premier appui sur « Écouter ». Un module vocal
    /// ne s'installe pas depuis Optimus : l'apprendre au moment de parler serait le découvrir
    /// trop tard.
    /// </summary>
    public string? LanguageWarning
    {
        get
        {
            string wanted = Language.Resolve(
                Language.Known.FirstOrDefault(l => Language.DisplayName(l) == _language));

            if (WindowsGrammarListener.HasRecognizer(wanted))
            {
                return null;
            }

            return Localization.Localizer.T("Settings.NoRecognizer", wanted);
        }
    }

    /// <summary>Vrai s'il manque quelque chose à Windows pour entendre la langue choisie.</summary>
    public bool HasLanguageWarning => LanguageWarning is not null;

    /// <summary>Copilotes installés, les vôtres masquant ceux qui sont livrés.</summary>
    public ObservableCollection<string> Copilots { get; } = new();

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
    public string ModeExplanation =>
        Localization.Localizer.T(PushToTalk ? "Settings.PttExplanation" : "Settings.AlwaysOnExplanation");

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
    public string ThresholdExplanation => Localization.Localizer.T(
        "Settings.ThresholdExplanation",
        NoiseFloor.ToString("F2"), ConfidenceThreshold.ToString("F2"));

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
                AlignVoiceToEngine();
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
        ? Localization.Localizer.T("Settings.PiperMissing", PiperInstallation.DefaultRoot)
        : Localization.Localizer.T(NeuralVoice ? "Settings.PiperOn" : "Settings.WindowsVoice");

    /// <summary>
    /// Copilote actif. L'affecter passe réellement la main, sans redémarrer.
    ///
    /// Contrairement aux autres réglages de cet écran, celui-ci ne passe pas par
    /// « Enregistrer » : changer de copilote change le mot d'éveil, la voix et le caractère
    /// d'un coup, et un aperçu à moitié appliqué n'aurait aucun sens — on veut l'entendre.
    /// </summary>
    public string? ActiveCopilot
    {
        get => _copilot;
        set
        {
            if (_switching || value is null || !Set(ref _copilot, value))
            {
                return;
            }

            _ = SwitchCopilotAsync(value);
        }
    }

    /// <summary>Nom du copilote à créer.</summary>
    public string CopilotName
    {
        get => _copilotName;
        set
        {
            if (Set(ref _copilotName, value))
            {
                DuplicateCopilotCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Vrai si le copilote actif vous appartient, donc peut être supprimé.</summary>
    public bool CanDeleteCopilot => _runtime.Copilots
        .Any(c => c.IsUsers && string.Equals(c.Id, _runtime.Copilot.Id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ce qu'un copilote recouvre, et ce que dupliquer veut dire.
    ///
    /// La confusion naturelle est de croire qu'un copilote n'est qu'une voix. Il porte aussi son
    /// mot d'éveil, son caractère et ses soixante-cinq répliques — c'est pourquoi on duplique
    /// plutôt que de partir de rien : un copilote sans répliques est un copilote muet.
    /// </summary>
    public string CopilotHint => Localization.Localizer.T(
        CanDeleteCopilot ? "Settings.CopilotMine" : "Settings.CopilotShipped",
        _runtime.Copilot.Name, _runtime.Copilot.WakeWord);

    /// <summary>Quand l'étage de parole libre intervient.</summary>
    public WhisperMode Whisper
    {
        get => _whisper;
        set
        {
            if (Track(ref _whisper, value))
            {
                Raise(nameof(WhisperOff));
                Raise(nameof(WhisperOnRejects));
                Raise(nameof(WhisperOnEverything));
                Raise(nameof(WhisperExplanation));
            }
        }
    }

    /// <summary>Les trois positions, liées à trois boutons radio.</summary>
    public bool WhisperOff
    {
        get => Whisper == WhisperMode.Off;
        set { if (value) { Whisper = WhisperMode.Off; } }
    }

    public bool WhisperOnRejects
    {
        get => Whisper == WhisperMode.Rejected;
        set { if (value) { Whisper = WhisperMode.Rejected; } }
    }

    public bool WhisperOnEverything
    {
        get => Whisper == WhisperMode.Always;
        set { if (value) { Whisper = WhisperMode.Always; } }
    }

    /// <summary>Réduire la fenêtre d'encodage : deux fois plus rapide, nettement moins sûr.</summary>
    public bool WhisperTrimContext
    {
        get => _trimContext;
        set
        {
            if (Track(ref _trimContext, value))
            {
                Raise(nameof(WhisperExplanation));
            }
        }
    }

    /// <summary>
    /// Ramène la voix choisie dans le moteur choisi.
    ///
    /// Décocher la voix neuronale laissait « fr_FR-tom-medium » sélectionné, que les voix
    /// Windows ne connaissent pas : l'essai parlait alors avec la voix système, en le signalant
    /// dans le journal mais pas à l'écran. Le pilote entendait une voix qu'il n'avait pas
    /// choisie et ne pouvait pas savoir pourquoi.
    /// </summary>
    private void AlignVoiceToEngine()
    {
        bool selectedIsNeural = IsNeural(VoiceId);

        if (selectedIsNeural == NeuralVoice)
        {
            return;
        }

        string? replacement = Voices.FirstOrDefault(v => IsNeural(v) == NeuralVoice);

        if (replacement is not null)
        {
            VoiceId = replacement;
        }
    }

    /// <summary>Cette voix appartient-elle au moteur neuronal ?</summary>
    private static bool IsNeural(string? voiceId) =>
        voiceId is not null
        && (PiperInstallation.Locate()?.Voices()
            .Any(v => string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase)) ?? false);

    /// <summary>Vrai si une installation de Whisper est utilisable.</summary>
    public bool WhisperAvailable => WhisperInstallation.Locate() is not null;

    /// <summary>
    /// Ce que chaque position coûte, en chiffres mesurés.
    ///
    /// Le pilote a demandé à pouvoir choisir « sur tout » : l'écran doit donc lui dire ce que ça
    /// vaut, sans l'enjoliver. Les nombres viennent du spike S0-7 et du spike S0-2 — d'une
    /// machine réelle, pas d'une brochure.
    /// </summary>
    public string WhisperExplanation
    {
        get
        {
            if (!WhisperAvailable)
            {
                return Localization.Localizer.T("Settings.WhisperMissing", WhisperInstallation.DefaultRoot);
            }

            string trimmed = WhisperTrimContext
                ? " " + Localization.Localizer.T("Settings.WhisperTrimmed")
                : string.Empty;

            return Whisper switch
            {
                WhisperMode.Rejected => Localization.Localizer.T("Settings.WhisperOnDoubt") + trimmed,
                WhisperMode.Always => Localization.Localizer.T("Settings.WhisperAlways") + trimmed,
                _ => Localization.Localizer.T("Settings.WhisperOff"),
            };
        }
    }

    /// <summary>L'API locale est-elle demandée ?</summary>
    public bool ApiEnabled
    {
        get => _apiEnabled;
        set
        {
            if (Track(ref _apiEnabled, value))
            {
                RegenerateTokenCommand.RaiseCanExecuteChanged();
                Raise(nameof(ApiExplanation));
            }
        }
    }

    public int ApiPort
    {
        get => _apiPort;
        set
        {
            if (Track(ref _apiPort, value))
            {
                Raise(nameof(ApiAddress));
            }
        }
    }

    public int ApiRate
    {
        get => _apiRate;
        set => Track(ref _apiRate, value);
    }

    /// <summary>Adresse d'écoute, telle qu'on la colle dans un client.</summary>
    public string ApiAddress => $"http://127.0.0.1:{ApiPort}/";

    /// <summary>Le jeton en vigueur, ou une invite à activer l'interface.</summary>
    public string ApiTokenSecret =>
        _runtime.ApiTokens.Count > 0
            ? _runtime.ApiTokens[0].Secret
            : Localization.Localizer.T("Settings.TokenPending");

    /// <summary>État réel du serveur, et non le réglage souhaité.</summary>
    public string ApiState => _runtime.Api is { IsRunning: true } api
        ? Localization.Localizer.T("Settings.ApiListening", api.Prefix)
        : Localization.Localizer.T(ApiEnabled ? "Settings.ApiOffPendingSave" : "Settings.ApiOff");

    /// <summary>
    /// Ce que l'API garantit, et ce qu'elle ne garantit pas.
    ///
    /// Les deux propriétés qui comptent sont dites ici, parce qu'un pilote qui ouvre une
    /// interface a le droit de savoir jusqu'où elle va — et où elle s'arrête.
    /// </summary>
    public string ApiExplanation =>
        Localization.Localizer.T(ApiEnabled ? "Settings.ApiOnExplanation" : "Settings.ApiOffExplanation");

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
        ? Localization.Localizer.T("Settings.AiOff")
        : AiProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? Localization.Localizer.T("Settings.AiLocal")
            : Localization.Localizer.T("Settings.AiRemote");

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

        _language = Language.DisplayName(_runtime.User.Language);

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

        RefreshCopilots();

        WhisperSettings whisper = _runtime.User.Whisper ?? WhisperSettings.Disabled;
        _whisper = whisper.Mode;
        _trimContext = whisper.TrimContext;

        ApiSettings api = _runtime.User.Api ?? ApiSettings.Disabled;
        _apiEnabled = api.Enabled;
        _apiPort = api.Port;
        _apiRate = api.ExecutionsPerMinute;

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

        SettingsWriter.SaveWhisper(_runtime.ProfilePath, new WhisperSettings(
            Whisper,
            (_runtime.User.Whisper ?? WhisperSettings.Disabled).Model,
            (_runtime.User.Whisper ?? WhisperSettings.Disabled).Threads,
            WhisperTrimContext));

        SettingsWriter.SaveApi(_runtime.ProfilePath, new ApiSettings(
            ApiEnabled, Math.Clamp(ApiPort, 1024, 65535), Math.Max(1, ApiRate)));

        SettingsWriter.SaveAi(_runtime.ProfilePath, new AiSettings(
            AiEnabled, AiProvider.Trim(), AiEndpoint.Trim(), AiModel.Trim(), Math.Max(1, AiBudget)));

        await _runtime.ReloadSettingsAsync().ConfigureAwait(true);

        // L'API suit le reglage : l'allumer, l'eteindre ou changer son port prend effet tout de
        // suite, sans redemarrer.
        _runtime.ApplyWhisperSettings();

        await _runtime.ApplyApiSettingsAsync().ConfigureAwait(true);

        IsDirty = false;
        RaiseAll();

        _log(Localization.Localizer.T("Log.SettingsSaved"),
            _runtime.IsListening ? Localization.Localizer.T("Log.ListeningRestarted") : null,
            ActivityLevel.Normal);
    }

    private async Task CaptureKeyAsync()
    {
        if (Owner is null)
        {
            return;
        }

        _log(Localization.Localizer.T("Log.PressPtt"), Localization.Localizer.T("Log.EscapeToCancel"), ActivityLevel.Speech);

        Core.Domain.Bindings.InputSpec? captured = await WindowKeyCapture
            .CaptureAsync(Owner, TimeSpan.FromSeconds(20))
            .ConfigureAwait(true);

        if (captured is null)
        {
            return;
        }

        if (!PushToTalkWatcher.IsSupported(captured.Key))
        {
            _log(Localization.Localizer.T("Log.KeyUnsuitable", captured.Key),
                Localization.Localizer.T("Log.KeyUnsuitableHint"),
                ActivityLevel.Warning);
            return;
        }

        PushToTalkKey = captured.Key;
    }

    private async Task TestVoiceAsync()
    {
        // On teste ce qui est A L'ECRAN, pas ce qui est enregistre : autrement, regler le debit
        // puis l'ecouter demanderait de sauvegarder d'abord, donc de valider a l'aveugle.
        //
        // LE MOTEUR AUSSI vient de l'ecran, et pas seulement la voix. C'etait le defaut signale
        // par le pilote le 2026-08-28 : choisir une voix Piper puis « Ecouter un essai » faisait
        // parler une voix Windows, parce que le moteur monte etait encore celui du copilote
        // ENREGISTRE. Il fallait sauvegarder pour entendre ce qu'on venait de choisir, soit
        // exactement l'inverse de ce que promet la phrase affichee sous le bouton.
        //
        // Un moteur jete apres usage, donc, comme le fait deja l'essai de connexion de l'etage
        // conversationnel. Monter Piper coute 0,6 s de chargement — c'est le prix d'entendre la
        // verite plutot qu'une approximation.
        Copilot preview = _runtime.Copilot with
        {
            Voice = _runtime.Copilot.Voice with
            {
                Provider = NeuralVoice ? SpeechFactory.Piper : "windows-onecore",
                VoiceId = VoiceId,
                Rate = Rate,
                Volume = Volume,
            },

            // Le caractere aussi : le curseur « calme » module le debit, et le pilote qui vient
            // de le deplacer doit l'entendre.
            Personality = _runtime.Copilot.Personality with { Traits = CurrentTraits() },
        };

        await using ITextToSpeechProvider engine = SpeechFactory.For(preview);

        await engine.SpeakAsync(new SpeechRequest(
            "Systèmes en ligne. À vos ordres, commandant.",
            preview.Voice.VoiceId,
            preview.EffectiveRate,
            preview.Voice.Volume))
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

    /// <summary>
    /// Émet un secret neuf pour le jeton du pilote.
    ///
    /// À faire dès qu'un jeton a pu fuiter — collé dans un salon, laissé dans un script. Les
    /// clients qui portaient l'ancien cesseront d'être admis, ce qui est le but.
    /// </summary>
    private void RegenerateToken()
    {
        try
        {
            ApiTokenStore.Regenerate(ApiTokenStore.OwnerName);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.TokenNotRenewed"), exception.Message, ActivityLevel.Warning);
            return;
        }

        _log(Localization.Localizer.T("Log.TokenRenewed"), Localization.Localizer.T("Log.TokenRenewedHint"),
            ActivityLevel.Warning);

        // Le serveur tient la liste chargee : sans relecture, l'ancien jeton continuerait
        // d'ouvrir la porte jusqu'au prochain demarrage.
        IsDirty = true;
        Raise(nameof(ApiTokenSecret));
    }

    /// <summary>Relit la liste des copilotes, sans déclencher de bascule.</summary>
    private void RefreshCopilots()
    {
        List<string> names = _runtime.Copilots.Select(c => c.Name).ToList();
        bool rebuilt = !Copilots.SequenceEqual(names, StringComparer.Ordinal);

        _switching = true;

        try
        {
            if (rebuilt)
            {
                Copilots.Clear();

                foreach (string name in names)
                {
                    Copilots.Add(name);
                }
            }

            _copilot = _runtime.Copilot.Name;
        }
        finally
        {
            _switching = false;
        }

        Raise(nameof(ActiveCopilot));
        Raise(nameof(CopilotHint));
        Raise(nameof(CanDeleteCopilot));
        DeleteCopilotCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Identifiant du copilote portant ce nom affiché.</summary>
    private string? IdOf(string name) => _runtime.Copilots
        .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))?.Id;

    private async Task SwitchLanguageAsync(string displayName)
    {
        if (Language.Known.FirstOrDefault(l => Language.DisplayName(l) == displayName)
            is not string language)
        {
            return;
        }

        try
        {
            await _runtime.SwitchLanguageAsync(language).ConfigureAwait(true);

            // Les mots de l'ecran suivent le moteur. Sans cette ligne, Optimus obeirait en
            // anglais derriere une interface restee francaise.
            Localization.Localizer.Apply(language);

            _log(Localization.Localizer.T("Log.Language", displayName),
                Localization.Localizer.T("Log.LanguageDetail", _runtime.Catalog.Count, _runtime.Copilot.Language),
                ActivityLevel.Normal);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.LanguageFailed"), exception.Message, ActivityLevel.Warning);
        }

        Raise(nameof(LanguageWarning));
        Raise(nameof(HasLanguageWarning));
        Revert();
    }

    private async Task SwitchCopilotAsync(string name)
    {
        if (IdOf(name) is not string id)
        {
            return;
        }

        try
        {
            await _runtime.SwitchCopilotAsync(id).ConfigureAwait(true);

            _log(Localization.Localizer.T("Log.Copilot", _runtime.Copilot.Name),
                Localization.Localizer.T("Log.CopilotDetail", _runtime.Copilot.WakeWord), ActivityLevel.Normal);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.HandoverFailed"), exception.Message, ActivityLevel.Warning);
        }

        RefreshCopilots();
        Revert();
    }

    private async Task DuplicateCopilotAsync()
    {
        string id;

        try
        {
            CopilotSet.Create(
                CopilotName, CopilotName.Trim(), _runtime.Copilot.Id, _runtime.DataRoot);

            id = CopilotSet.Sanitize(CopilotName);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.CopilotNotCreated"), exception.Message, ActivityLevel.Warning);
            return;
        }

        _log(Localization.Localizer.T("Log.CopilotCreated", CopilotName.Trim()),
            Localization.Localizer.T("Log.CopilotCreatedDetail", _runtime.Copilot.Name),
            ActivityLevel.Normal);

        CopilotName = string.Empty;

        try
        {
            await _runtime.SwitchCopilotAsync(id).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.HandoverFailed"), exception.Message, ActivityLevel.Warning);
        }

        RefreshCopilots();
        Revert();
    }

    private async Task DeleteCopilotAsync()
    {
        string doomed = _runtime.Copilot.Id;
        string name = _runtime.Copilot.Name;

        try
        {
            CopilotSet.Delete(doomed);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.DeleteFailed"), exception.Message, ActivityLevel.Warning);
            return;
        }

        _log(Localization.Localizer.T("Log.CopilotDeleted", name),
            Localization.Localizer.T("Log.CopilotDeletedDetail"), ActivityLevel.Warning);

        // Le copilote actif vient de disparaitre : en reprendre un, sans quoi Optimus resterait
        // pointe sur un dossier qui n'existe plus.
        try
        {
            await _runtime.SwitchCopilotAsync(
                CopilotSet.Resolve(null, _runtime.DataRoot)).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _log(Localization.Localizer.T("Log.NoFallbackCopilot"), exception.Message, ActivityLevel.Warning);
        }

        RefreshCopilots();
        Revert();
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
            nameof(ActiveCopilot), nameof(CopilotName), nameof(CopilotHint), nameof(CanDeleteCopilot),
            nameof(NeuralVoice), nameof(NeuralVoiceAvailable), nameof(VoiceEngineExplanation),
            nameof(Whisper), nameof(WhisperOff), nameof(WhisperOnRejects),
            nameof(WhisperOnEverything), nameof(WhisperTrimContext),
            nameof(WhisperAvailable), nameof(WhisperExplanation),
            nameof(ApiEnabled), nameof(ApiPort), nameof(ApiRate), nameof(ApiAddress),
            nameof(ApiTokenSecret), nameof(ApiState), nameof(ApiExplanation),
            nameof(AiEnabled), nameof(AiProvider), nameof(AiEndpoint), nameof(AiModel),
            nameof(AiBudget), nameof(AiExplanation), nameof(AiProbe),
        })
        {
            Raise(property);
        }
    }
}
