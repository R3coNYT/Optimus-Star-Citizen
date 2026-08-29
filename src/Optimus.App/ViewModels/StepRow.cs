using Optimus.App.Mvvm;
using Optimus.Core.Domain.Commands;

namespace Optimus.App.ViewModels;

/// <summary>
/// Rôle d'affichage d'une ligne.
///
/// Les blocs forment un arbre, la liste est plate : deux repères rendent visible ce que
/// l'indentation seule laisserait deviner, et surtout ils donnent un <b>point d'insertion</b>.
/// Sans ligne « sinon », rien ne permettrait d'ajouter une étape à une branche encore vide.
/// </summary>
public enum RowMarker
{
    /// <summary>Une vraie étape.</summary>
    Step,

    /// <summary>Séparateur « sinon » d'un « si ».</summary>
    Else,

    /// <summary>Fin d'un bloc.</summary>
    End,
}

/// <summary>
/// Une étape en cours d'édition. Mutable, contrairement à <see cref="ActionStep"/>.
///
/// Porte ses propres enfants : un « si » ou un « répéter » possède son bloc, et l'écran n'en
/// montre qu'une projection à plat. C'est l'arbre qu'on édite, jamais la projection — un bloc
/// qu'aucune opération n'a ouvert ne peut pas rester ouvert, et l'écran ne peut donc pas
/// produire une structure que le moteur refuserait.
/// </summary>
public sealed class StepRow : ObservableObject
{
    private ActionStepType _type = ActionStepType.Command;
    private string? _commandId;
    private CommandPolarity _polarity = CommandPolarity.Neutral;
    private bool _requireDirected;
    private int _waitMs = 500;
    private string? _responseKey;
    private int _times = 2;
    private ConditionSubject _subject = ConditionSubject.Binding;
    private string? _conditionCommandId;
    private CommandPolarity _conditionPolarity = CommandPolarity.On;
    private string? _conditionValue = "scm";
    private bool _conditionNegated;
    private int _depth;

    /// <summary>Repère d'affichage. Un repère n'est jamais enregistré.</summary>
    public RowMarker Marker { get; init; } = RowMarker.Step;

    /// <summary>Étape à laquelle ce repère appartient.</summary>
    public StepRow? Owner { get; init; }

    /// <summary>Bloc principal : le « alors » d'un si, le corps d'un répéter.</summary>
    public List<StepRow> Block { get; } = new();

    /// <summary>Bloc « sinon ».</summary>
    public List<StepRow> Alternative { get; } = new();

    /// <summary>Profondeur d'imbrication, pour l'indentation.</summary>
    public int Depth
    {
        get => _depth;
        set => Set(ref _depth, value);
    }

    public ActionStepType Type
    {
        get => _type;
        set
        {
            if (Set(ref _type, value))
            {
                RaiseShape();
            }
        }
    }

    public string? CommandId
    {
        get => _commandId;
        set
        {
            if (Set(ref _commandId, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public CommandPolarity Polarity
    {
        get => _polarity;
        set
        {
            if (Set(ref _polarity, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public bool RequireDirected
    {
        get => _requireDirected;
        set
        {
            if (Set(ref _requireDirected, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public int WaitMs
    {
        get => _waitMs;
        set
        {
            if (Set(ref _waitMs, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public string? ResponseKey
    {
        get => _responseKey;
        set
        {
            if (Set(ref _responseKey, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    /// <summary>Nombre de tours d'une répétition.</summary>
    public int Times
    {
        get => _times;
        set
        {
            if (Set(ref _times, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public ConditionSubject Subject
    {
        get => _subject;
        set
        {
            if (Set(ref _subject, value))
            {
                RaiseCondition();
            }
        }
    }

    public string? ConditionCommandId
    {
        get => _conditionCommandId;
        set
        {
            if (Set(ref _conditionCommandId, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public CommandPolarity ConditionPolarity
    {
        get => _conditionPolarity;
        set
        {
            if (Set(ref _conditionPolarity, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public string? ConditionValue
    {
        get => _conditionValue;
        set
        {
            if (Set(ref _conditionValue, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public bool ConditionNegated
    {
        get => _conditionNegated;
        set
        {
            if (Set(ref _conditionNegated, value))
            {
                Raise(nameof(Summary));
            }
        }
    }

    public bool IsMarker => Marker != RowMarker.Step;

    public bool IsCommand => Marker == RowMarker.Step && Type == ActionStepType.Command;

    public bool IsWait => Marker == RowMarker.Step && Type == ActionStepType.Wait;

    public bool IsSay => Marker == RowMarker.Step && Type == ActionStepType.Say;

    public bool IsBranch => Marker == RowMarker.Step && Type == ActionStepType.If;

    public bool IsLoop => Marker == RowMarker.Step && Type == ActionStepType.Repeat;

    /// <summary>Le sujet interrogé désigne-t-il une commande ?</summary>
    public bool ConditionNeedsCommand => IsBranch && Subject
        is ConditionSubject.Binding or ConditionSubject.Directed or ConditionSubject.Believed;

    /// <summary>Le sujet interrogé attend-il un sens ?</summary>
    public bool ConditionNeedsPolarity => IsBranch && Subject == ConditionSubject.Directed;

    /// <summary>Le sujet interrogé attend-il une valeur à choisir ?</summary>
    public bool ConditionNeedsValue => IsBranch && Subject
        is ConditionSubject.FlightMode or ConditionSubject.Believed;

    /// <summary>Valeurs proposées pour le sujet courant.</summary>
    public IReadOnlyList<string> ConditionValues => Subject switch
    {
        ConditionSubject.FlightMode => ["nav", "scm"],
        ConditionSubject.Believed => ["on", "off"],
        _ => Array.Empty<string>(),
    };

    /// <summary>
    /// Ce que le sujet vaut réellement, dit sans détour.
    ///
    /// Deux des cinq sujets sont des croyances, et la différence ne se devine pas depuis un nom
    /// de liste déroulante. Un pilote qui branche sur un état supposé doit l'apprendre en
    /// l'écrivant, pas en voyant sa macro se tromper.
    /// </summary>
    public string SubjectCaveat => Localization.Localizer.T(Subject switch
    {
        ConditionSubject.Binding => "Macros.CaveatBinding",
        ConditionSubject.Directed => "Macros.CaveatDirected",
        ConditionSubject.Simulation => "Macros.CaveatSimulation",
        ConditionSubject.FlightMode => "Macros.CaveatFlightMode",
        _ => "Macros.CaveatBelieved",
    });

    /// <summary>Ligne lisible dans la liste : ce que fera l'étape, dans la langue affichée.</summary>
    public string Summary => Marker switch
    {
        RowMarker.Else => Localization.Localizer.T("Macros.StepElse"),
        RowMarker.End => Localization.Localizer.T("Macros.StepEnd"),
        _ => Type switch
        {
            ActionStepType.Wait => Localization.Localizer.T("Macros.StepWait", WaitMs),
            ActionStepType.Say => Localization.Localizer.T("Macros.StepSay", ResponseKey ?? "?"),
            ActionStepType.If => Localization.Localizer.T("Macros.StepIf", DescribeCondition()),
            ActionStepType.Repeat => Localization.Localizer.T("Macros.StepRepeat", Times),
            _ => Localization.Localizer.T(
                     Polarity switch
                     {
                         CommandPolarity.On => "Macros.StepOn",
                         CommandPolarity.Off => "Macros.StepOff",
                         _ => "Macros.StepToggle",
                     },
                     CommandId ?? "?")
                 + (RequireDirected ? Localization.Localizer.T("Macros.StepDirected") : string.Empty),
        },
    };

    /// <summary>
    /// La condition, écrite pour l'écran.
    ///
    /// <see cref="MacroCondition.Describe"/> existe déjà et dit la même chose — mais en
    /// français, et le moteur n'a pas de dictionnaire : il écrit dans le journal, pas dans une
    /// fenêtre. Refaire ici l'aiguillage n'est donc pas une duplication de règle métier, c'est
    /// la même donnée rendue pour un autre lecteur.
    ///
    /// Une clé par cas, plutôt qu'une phrase assemblée de morceaux. « n'a pas » ne se glisse
    /// pas au même endroit qu'un « is missing », et une phrase à trous se casse à la
    /// deuxième langue.
    /// </summary>
    private string DescribeCondition()
    {
        string command = ConditionNeedsCommand ? ConditionCommandId ?? "?" : "?";
        string value = ConditionValue ?? string.Empty;

        return Subject switch
        {
            ConditionSubject.Binding => Localization.Localizer.T(
                ConditionNegated ? "Macros.CondBindingNo" : "Macros.CondBindingYes", command),

            ConditionSubject.Directed => Localization.Localizer.T(
                (ConditionNegated, ConditionPolarity == CommandPolarity.Off) switch
                {
                    (false, false) => "Macros.CondDirectedOn",
                    (false, true) => "Macros.CondDirectedOff",
                    (true, false) => "Macros.CondDirectedNotOn",
                    (true, true) => "Macros.CondDirectedNotOff",
                },
                command),

            ConditionSubject.Simulation => Localization.Localizer.T(
                ConditionNegated ? "Macros.CondReal" : "Macros.CondSimulated"),

            ConditionSubject.FlightMode => Localization.Localizer.T(
                ConditionNegated ? "Macros.CondFlightModeNot" : "Macros.CondFlightMode",
                value.ToUpperInvariant()),

            ConditionSubject.Believed => Localization.Localizer.T(
                ConditionNegated ? "Macros.CondBelievedNot" : "Macros.CondBelieved",
                command, value),

            _ => Localization.Localizer.T("Macros.CondUnknown"),
        };
    }

    /// <summary>Condition telle que le moteur la recevra.</summary>
    public MacroCondition BuildCondition() => new(
        Subject,
        ConditionNegated,
        ConditionNeedsCommand ? ConditionCommandId : null,
        ConditionNeedsPolarity ? ConditionPolarity : CommandPolarity.Neutral,
        ConditionNeedsValue ? ConditionValue : null);

    public ActionStep ToStep() => Type switch
    {
        ActionStepType.Wait => ActionStep.Wait(WaitMs),
        ActionStepType.Say => new ActionStep(ActionStepType.Say, ResponseKey: ResponseKey),
        ActionStepType.If => ActionStep.When(
            BuildCondition(),
            Block.Select(r => r.ToStep()).ToList(),
            Alternative.Count == 0 ? null : Alternative.Select(r => r.ToStep()).ToList()),
        ActionStepType.Repeat => ActionStep.Loop(Times, Block.Select(r => r.ToStep()).ToList()),
        _ => ActionStep.Call(CommandId ?? string.Empty, Polarity, RequireDirected),
    };

    public static StepRow From(ActionStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        StepRow row = new()
        {
            _type = step.Type,
            _commandId = step.CommandId,
            _polarity = step.Polarity,
            _requireDirected = step.RequireDirected,
            _waitMs = step.WaitMs > 0 ? step.WaitMs : 500,
            _responseKey = step.ResponseKey,
            _times = step.Type == ActionStepType.Repeat ? step.Repeat : 2,
        };

        if (step.Condition is MacroCondition condition)
        {
            row._subject = condition.Subject;
            row._conditionCommandId = condition.CommandId;
            row._conditionNegated = condition.Negated;
            row._conditionValue = condition.Value
                ?? (condition.Subject == ConditionSubject.Believed ? "on" : "scm");

            if (condition.Polarity != CommandPolarity.Neutral)
            {
                row._conditionPolarity = condition.Polarity;
            }
        }

        foreach (ActionStep child in step.Block)
        {
            row.Block.Add(From(child));
        }

        foreach (ActionStep child in step.Alternative)
        {
            row.Alternative.Add(From(child));
        }

        return row;
    }

    private void RaiseShape()
    {
        Raise(nameof(IsCommand));
        Raise(nameof(IsWait));
        Raise(nameof(IsSay));
        Raise(nameof(IsBranch));
        Raise(nameof(IsLoop));
        RaiseCondition();
    }

    private void RaiseCondition()
    {
        Raise(nameof(ConditionNeedsCommand));
        Raise(nameof(ConditionNeedsPolarity));
        Raise(nameof(ConditionNeedsValue));
        Raise(nameof(ConditionValues));
        Raise(nameof(SubjectCaveat));
        Raise(nameof(Summary));
    }
}
