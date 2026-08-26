using System.Collections.ObjectModel;
using System.Windows;
using Optimus.App.Mvvm;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Execution;
using Optimus.Core.Loading;
using Optimus.Infrastructure.Hosting;

namespace Optimus.App.ViewModels;

/// <summary>Une étape en cours d'édition. Mutable, contrairement à <see cref="ActionStep"/>.</summary>
public sealed class StepRow : ObservableObject
{
    private ActionStepType _type = ActionStepType.Command;
    private string? _commandId;
    private CommandPolarity _polarity = CommandPolarity.Neutral;
    private bool _requireDirected;
    private int _waitMs = 500;
    private string? _responseKey;

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

    public bool IsCommand => Type == ActionStepType.Command;

    public bool IsWait => Type == ActionStepType.Wait;

    public bool IsSay => Type == ActionStepType.Say;

    /// <summary>Ligne lisible dans la liste : ce que fera l'étape, en français.</summary>
    public string Summary => Type switch
    {
        ActionStepType.Wait => $"attendre {WaitMs} ms",
        ActionStepType.Say => $"dire « {ResponseKey} »",
        _ => (Polarity switch
        {
            CommandPolarity.On => "allumer ",
            CommandPolarity.Off => "éteindre ",
            _ => "basculer ",
        }) + (CommandId ?? "?") + (RequireDirected ? "   [sens garanti exigé]" : string.Empty),
    };

    public ActionStep ToStep() => Type switch
    {
        ActionStepType.Wait => ActionStep.Wait(WaitMs),
        ActionStepType.Say => new ActionStep(ActionStepType.Say, ResponseKey: ResponseKey),
        _ => ActionStep.Call(CommandId ?? string.Empty, Polarity, RequireDirected),
    };

    public static StepRow From(ActionStep step) => new()
    {
        _type = step.Type,
        _commandId = step.CommandId,
        _polarity = step.Polarity,
        _requireDirected = step.RequireDirected,
        _waitMs = step.WaitMs > 0 ? step.WaitMs : 500,
        _responseKey = step.ResponseKey,
    };

    private void RaiseShape()
    {
        Raise(nameof(IsCommand));
        Raise(nameof(IsWait));
        Raise(nameof(IsSay));
        Raise(nameof(Summary));
    }
}

/// <summary>Une macro dans la liste : la sienne, ou celle qui est livrée.</summary>
public sealed class MacroRow(CommandDefinition macro, bool isUsers)
{
    public CommandDefinition Macro { get; } = macro;

    public string Id => Macro.Id;

    public string Name => Macro.Name;

    public bool IsUsers { get; } = isUsers;

    public string Origin => IsUsers ? "à vous" : "livrée";

    public string Summary => $"{Macro.Actions.Count} étapes · {Macro.VoicePhrases.Count} formulations";
}

/// <summary>
/// L'éditeur de macros.
///
/// Les macros écrites ici vivent dans les données de l'utilisateur, jamais dans le catalogue
/// livré — celui-ci est remplacé à chaque publication. Modifier une macro livrée en crée une
/// copie qui la remplace : l'originale reste sur disque, intacte, et « Revenir à la version
/// livrée » la restitue.
///
/// Rien n'est enregistré sans avoir été vérifié. Une macro incohérente écrite sur disque
/// empêcherait le catalogue entier de se charger au démarrage suivant, et le pilote se
/// retrouverait devant un Optimus muet sans savoir pourquoi.
/// </summary>
public sealed class MacroEditorViewModel : ObservableObject
{
    private readonly OptimusRuntime _runtime;
    private readonly Action<string, string?, ActivityLevel> _log;
    private readonly Func<Task> _afterChange;

    private MacroRow? _selected;
    private string _name = string.Empty;
    private string _phrases = string.Empty;
    private int _cooldownMs = 4000;
    private StepRow? _selectedStep;
    private string _verdict = string.Empty;
    private bool _dirty;

    public MacroEditorViewModel(
        OptimusRuntime runtime,
        Action<string, string?, ActivityLevel> log,
        Func<Task> afterChange)
    {
        _runtime = runtime;
        _log = log;
        _afterChange = afterChange;

        NewCommand = new RelayCommand(CreateNew);
        DuplicateCommand = new RelayCommand(Duplicate, () => Selected is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => Selected?.IsUsers == true);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsDirty && Selected is not null);
        RevertCommand = new RelayCommand(LoadSelected, () => IsDirty);
        TestCommand = new AsyncRelayCommand(TestAsync, () => Selected is not null && !IsDirty);

        AddCommandStepCommand = new RelayCommand(() => AddStep(ActionStepType.Command));
        AddWaitStepCommand = new RelayCommand(() => AddStep(ActionStepType.Wait));
        AddSayStepCommand = new RelayCommand(() => AddStep(ActionStepType.Say));
        RemoveStepCommand = new RelayCommand(RemoveStep, () => SelectedStep is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => CanMove(-1));
        MoveDownCommand = new RelayCommand(() => Move(1), () => CanMove(1));

        Refresh();
    }

    public ObservableCollection<MacroRow> Macros { get; } = new();

    public ObservableCollection<StepRow> Steps { get; } = new();

    /// <summary>Commandes qu'une étape peut appeler, macros exclues pour limiter les cycles.</summary>
    public ObservableCollection<string> Callable { get; } = new();

    public RelayCommand NewCommand { get; }

    public RelayCommand DuplicateCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand RevertCommand { get; }

    public AsyncRelayCommand TestCommand { get; }

    public RelayCommand AddCommandStepCommand { get; }

    public RelayCommand AddWaitStepCommand { get; }

    public RelayCommand AddSayStepCommand { get; }

    public RelayCommand RemoveStepCommand { get; }

    public RelayCommand MoveUpCommand { get; }

    public RelayCommand MoveDownCommand { get; }

    public MacroRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                LoadSelected();
                DuplicateCommand.RaiseCanExecuteChanged();
                DeleteCommand.RaiseCanExecuteChanged();
                TestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public StepRow? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (Set(ref _selectedStep, value))
            {
                RemoveStepCommand.RaiseCanExecuteChanged();
                MoveUpCommand.RaiseCanExecuteChanged();
                MoveDownCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, value))
            {
                Touch();
            }
        }
    }

    /// <summary>Une formulation par ligne : plus lisible qu'une liste de champs à cinq entrées.</summary>
    public string Phrases
    {
        get => _phrases;
        set
        {
            if (Set(ref _phrases, value))
            {
                Touch();
            }
        }
    }

    public int CooldownMs
    {
        get => _cooldownMs;
        set
        {
            if (Set(ref _cooldownMs, value))
            {
                Touch();
            }
        }
    }

    /// <summary>Résultat de la dernière vérification, ou vide.</summary>
    public string Verdict
    {
        get => _verdict;
        private set => Set(ref _verdict, value);
    }

    public bool IsDirty
    {
        get => _dirty;
        private set
        {
            if (Set(ref _dirty, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
                TestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanEdit => Selected is not null;

    /// <summary>Recharge la liste depuis le catalogue courant.</summary>
    public void Refresh()
    {
        string? keep = Selected?.Id;

        Macros.Clear();

        foreach (CommandDefinition macro in _runtime.Catalog.Commands
            .Where(c => c.Kind == CommandKind.Macro)
            .OrderBy(c => c.Name, StringComparer.CurrentCulture))
        {
            bool shipped = _runtime.ShippedCatalog.TryGet(macro.Id, out CommandDefinition? original)
                           && original is not null;

            Macros.Add(new MacroRow(macro, !shipped || !ReferenceEquals(original, macro)));
        }

        Callable.Clear();

        // Les macros ne s'appellent pas entre elles depuis l'editeur : le depliage sait gerer
        // un niveau de renvoi, mais offrir le cycle dans une liste deroulante serait une
        // invitation a l'ecrire.
        foreach (CommandDefinition command in _runtime.Catalog.Commands
            .Where(c => c.Kind != CommandKind.Macro && !c.IsPassive)
            .OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            Callable.Add(command.Id);
        }

        Selected = Macros.FirstOrDefault(m => m.Id == keep) ?? Macros.FirstOrDefault();
    }

    private void LoadSelected()
    {
        Steps.Clear();

        if (Selected is null)
        {
            _name = string.Empty;
            _phrases = string.Empty;
            _cooldownMs = 4000;
        }
        else
        {
            CommandDefinition macro = Selected.Macro;
            _name = macro.Name;
            _phrases = string.Join(Environment.NewLine, macro.VoicePhrases);
            _cooldownMs = macro.CooldownMs;

            foreach (ActionStep step in macro.Actions)
            {
                Steps.Add(Track(StepRow.From(step)));
            }
        }

        Verdict = string.Empty;
        IsDirty = false;

        Raise(nameof(Name));
        Raise(nameof(Phrases));
        Raise(nameof(CooldownMs));
        Raise(nameof(CanEdit));
    }

    private void CreateNew()
    {
        CommandDefinition draft = new(
            $"macro.perso.{DateTime.Now:yyyyMMddHHmmss}",
            CommandKind.Macro,
            "Nouvelle macro",
            "macro",
            ["ma nouvelle macro"],
            [ActionStep.Wait(500)],
            CooldownMs: 4000);

        Macros.Add(new MacroRow(draft, isUsers: true));
        Selected = Macros[^1];
        IsDirty = true;

        _log("macro créée", "Nommez-la, donnez-lui des formulations, puis enregistrez.",
            ActivityLevel.Normal);
    }

    private void Duplicate()
    {
        if (Selected is null)
        {
            return;
        }

        CommandDefinition source = Selected.Macro;

        CommandDefinition copy = source with
        {
            Id = $"macro.perso.{DateTime.Now:yyyyMMddHHmmss}",
            Name = source.Name + " (copie)",

            // Les formulations ne se dupliquent pas : deux commandes ne peuvent pas repondre au
            // meme enonce, et la copie serait refusee a l'enregistrement.
            VoicePhrases = [],
        };

        Macros.Add(new MacroRow(copy, isUsers: true));
        Selected = Macros[^1];
        IsDirty = true;

        _log("macro dupliquée", "Donnez-lui ses propres formulations avant d'enregistrer.",
            ActivityLevel.Normal);
    }

    private async Task DeleteAsync()
    {
        if (Selected is not MacroRow row || !row.IsUsers)
        {
            return;
        }

        bool shipped = _runtime.ShippedCatalog.Contains(row.Id);

        string question = shipped
            ? $"« {row.Name} » reviendra à la version livrée avec Optimus.\n\nContinuer ?"
            : $"« {row.Name} » sera supprimée définitivement.\n\nContinuer ?";

        if (MessageBox.Show(question, "Optimus", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK)
        {
            return;
        }

        IEnumerable<CommandDefinition> remaining = UserDefined().Where(
            m => !string.Equals(m.Id, row.Id, StringComparison.OrdinalIgnoreCase));

        UserMacros.Save(_runtime.MacroPath, remaining);
        await _afterChange().ConfigureAwait(true);

        _log(shipped ? $"« {row.Name} » revenue à la version livrée" : $"« {row.Name} » supprimée",
            null, ActivityLevel.Normal);

        Refresh();
    }

    private async Task SaveAsync()
    {
        if (Selected is null)
        {
            return;
        }

        CommandDefinition edited = Build();

        // Le catalogue de reference exclut la macro en cours : sans quoi elle se reprocherait
        // ses propres formulations.
        CommandCatalog others = new(
            _runtime.Catalog.Id,
            _runtime.Catalog.Name,
            _runtime.Catalog.Commands.Where(
                c => !string.Equals(c.Id, edited.Id, StringComparison.OrdinalIgnoreCase)));

        MacroValidator.Verdict verdict = MacroValidator.Check(edited, others, _runtime.Bindings);

        if (!verdict.IsValid)
        {
            Verdict = "Impossible d'enregistrer :\n• " + string.Join("\n• ", verdict.Errors);
            return;
        }

        List<CommandDefinition> macros = UserDefined()
            .Where(m => !string.Equals(m.Id, edited.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        macros.Add(edited);

        UserMacros.Save(_runtime.MacroPath, macros);
        await _afterChange().ConfigureAwait(true);

        Verdict = verdict.Warnings.Count == 0
            ? "Enregistrée."
            : "Enregistrée. À savoir :\n• " + string.Join("\n• ", verdict.Warnings);

        _log($"macro « {edited.Name} » enregistrée",
            verdict.Warnings.Count == 0 ? null : verdict.Warnings[0],
            verdict.Warnings.Count == 0 ? ActivityLevel.Normal : ActivityLevel.Warning);

        IsDirty = false;
        Refresh();
    }

    private async Task TestAsync()
    {
        if (Selected is null)
        {
            return;
        }

        // Essayer une macro de dix pas sur un vaisseau en vol sans l'avoir vue se derouler
        // serait imprudent : on force la simulation le temps de l'essai.
        bool wasReal = !_runtime.SimulationMode;

        if (wasReal)
        {
            _runtime.SetSimulation(true);
        }

        try
        {
            await _runtime.RunCommandAsync(Selected.Macro).ConfigureAwait(true);
        }
        finally
        {
            if (wasReal)
            {
                _runtime.SetSimulation(false);
            }
        }

        _log($"essai de « {Selected.Name} »",
            "Joué en simulation : la trace est dans le journal, aucune touche n'est partie.",
            ActivityLevel.Muted);
    }

    /// <summary>Macros appartenant au pilote, telles qu'elles doivent être réécrites sur disque.</summary>
    private IEnumerable<CommandDefinition> UserDefined()
    {
        LoadResult<CommandCatalog> stored = UserMacros.Load(_runtime.MacroPath);
        return stored.Value.Commands;
    }

    private CommandDefinition Build()
    {
        string[] phrases = Phrases
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();

        return new CommandDefinition(
            Selected!.Id,
            CommandKind.Macro,
            Name.Trim(),
            "macro",
            phrases,
            Steps.Select(s => s.ToStep()).ToList(),
            CooldownMs: Math.Max(0, CooldownMs));
    }

    private void AddStep(ActionStepType type)
    {
        StepRow row = Track(new StepRow
        {
            Type = type,
            CommandId = type == ActionStepType.Command ? Callable.FirstOrDefault() : null,
            ResponseKey = type == ActionStepType.Say ? "system.success" : null,
        });

        int index = SelectedStep is null ? Steps.Count : Steps.IndexOf(SelectedStep) + 1;
        Steps.Insert(index, row);
        SelectedStep = row;
        Touch();
    }

    private void RemoveStep()
    {
        if (SelectedStep is not StepRow row)
        {
            return;
        }

        int index = Steps.IndexOf(row);
        Steps.Remove(row);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(index, Steps.Count - 1)];
        Touch();
    }

    private bool CanMove(int delta)
    {
        if (SelectedStep is null)
        {
            return false;
        }

        int target = Steps.IndexOf(SelectedStep) + delta;
        return target >= 0 && target < Steps.Count;
    }

    private void Move(int delta)
    {
        if (SelectedStep is not StepRow row)
        {
            return;
        }

        int index = Steps.IndexOf(row);
        Steps.Move(index, index + delta);
        SelectedStep = row;

        MoveUpCommand.RaiseCanExecuteChanged();
        MoveDownCommand.RaiseCanExecuteChanged();
        Touch();
    }

    /// <summary>Une étape modifiée salit la macro : sans cela « Enregistrer » resterait grisé.</summary>
    private StepRow Track(StepRow row)
    {
        row.PropertyChanged += (_, _) => Touch();
        return row;
    }

    private void Touch()
    {
        IsDirty = true;
        Verdict = string.Empty;
    }
}
