using System.Collections.ObjectModel;
using Optimus.App.Mvvm;
using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;
using Optimus.Core.Loading;
using Optimus.Infrastructure.Hosting;

namespace Optimus.App.ViewModels;

/// <summary>Une hésitation, telle que la liste l'affiche.</summary>
public sealed class HesitationRow(Hesitation hesitation, string? commandName)
{
    public Hesitation Source { get; } = hesitation;

    public string Heard => Source.Heard;

    public int Count => Source.Count;

    public string CommandName { get; } = commandName ?? "aucune";

    public string? CommandId => Source.CommandId;

    public string Kind => Source.Kind switch
    {
        HesitationKind.Proposed => "proposée sans certitude",
        HesitationKind.Denied => "proposition refusée",
        HesitationKind.Ambiguous => "plusieurs commandes",
        _ => "non comprise",
    };

    public string Detail =>
        $"{Count} fois · confiance {Source.LastConfidence:F2} · {Source.LastSeen.ToLocalTime():dd/MM HH:mm}";
}

/// <summary>Une formulation déjà ajoutée par le pilote.</summary>
public sealed class AliasRow(PhraseAlias alias, string? commandName)
{
    public PhraseAlias Source { get; } = alias;

    public string Phrase => Source.Phrase;

    public string CommandName { get; } = commandName ?? alias.CommandId;

    public string Sense => Source.Polarity switch
    {
        CommandPolarity.On => "activation",
        CommandPolarity.Off => "extinction",
        _ => "—",
    };
}

/// <summary>
/// Ce qu'Optimus n'a pas compris, et ce qu'on peut lui apprendre.
///
/// <b>La limite à garder en tête</b> : la grammaire est fermée. Le moteur ne peut rendre qu'une
/// formulation qu'il connaît déjà ; ce que le pilote a réellement dit ne lui parvient jamais.
/// La liste dit donc « j'ai hésité N fois autour de cette commande », pas « vous avez dit ceci ».
/// C'est au pilote d'écrire la tournure qu'il emploie — Optimus ne peut que désigner l'endroit
/// du problème.
///
/// L'écran n'a pas d'autre ambition, et c'est déjà beaucoup : sans lui, chaque formulation
/// manquante attendait une passe de développement.
/// </summary>
public sealed class UnderstandingViewModel : ObservableObject
{
    private readonly OptimusRuntime _runtime;
    private readonly Action<string, string?, ActivityLevel> _log;
    private readonly Func<Task> _afterChange;

    private HesitationRow? _selected;
    private string _phrase = string.Empty;
    private string? _targetCommand;
    private CommandPolarity _polarity = CommandPolarity.Neutral;
    private string _verdict = string.Empty;

    public UnderstandingViewModel(
        OptimusRuntime runtime,
        Action<string, string?, ActivityLevel> log,
        Func<Task> afterChange)
    {
        _runtime = runtime;
        _log = log;
        _afterChange = afterChange;

        AddCommand = new AsyncRelayCommand(AddAsync, CanAdd);
        IgnoreCommand = new RelayCommand(Ignore, () => Selected is not null);
        ClearCommand = new RelayCommand(ClearAll, () => Hesitations.Count > 0);
        RemoveAliasCommand = new AsyncRelayCommand(RemoveAliasAsync, () => SelectedAlias is not null);

        Refresh();
    }

    public ObservableCollection<HesitationRow> Hesitations { get; } = new();

    public ObservableCollection<AliasRow> Aliases { get; } = new();

    public ObservableCollection<string> Commands { get; } = new();

    public AsyncRelayCommand AddCommand { get; }

    public RelayCommand IgnoreCommand { get; }

    public RelayCommand ClearCommand { get; }

    public AsyncRelayCommand RemoveAliasCommand { get; }

    public HesitationRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
            {
                return;
            }

            // On pré-remplit avec ce qu'Optimus a cru entendre et la commande qu'il envisageait.
            // C'est un point de départ, pas une vérité : le pilote corrige.
            Phrase = value?.Heard ?? string.Empty;
            TargetCommand = value?.CommandId ?? Commands.FirstOrDefault();
            Verdict = string.Empty;

            IgnoreCommand.RaiseCanExecuteChanged();
        }
    }

    private AliasRow? _selectedAlias;

    public AliasRow? SelectedAlias
    {
        get => _selectedAlias;
        set
        {
            if (Set(ref _selectedAlias, value))
            {
                RemoveAliasCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>La tournure que le pilote emploie réellement.</summary>
    public string Phrase
    {
        get => _phrase;
        set
        {
            if (Set(ref _phrase, value))
            {
                Verdict = string.Empty;
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? TargetCommand
    {
        get => _targetCommand;
        set
        {
            if (Set(ref _targetCommand, value))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public CommandPolarity Polarity
    {
        get => _polarity;
        set => Set(ref _polarity, value);
    }

    public string Verdict
    {
        get => _verdict;
        private set => Set(ref _verdict, value);
    }

    public string Summary => Hesitations.Count == 0
        ? "Rien à signaler : tout ce qu'Optimus a entendu, il l'a compris."
        : $"{Hesitations.Count} formulation(s) sur lesquelles Optimus a hésité · "
          + $"{Aliases.Count} ajoutée(s) par vous";

    /// <summary>Recharge la liste depuis le journal et les formulations enregistrées.</summary>
    public void Refresh()
    {
        Hesitations.Clear();

        foreach (Hesitation hesitation in _runtime.Understanding.Entries)
        {
            string? name = hesitation.CommandId is not null
                && _runtime.Catalog.TryGet(hesitation.CommandId, out CommandDefinition? command)
                ? command!.Name
                : null;

            Hesitations.Add(new HesitationRow(hesitation, name));
        }

        Aliases.Clear();

        foreach (PhraseAlias alias in _runtime.Aliases)
        {
            string? name = _runtime.Catalog.TryGet(alias.CommandId, out CommandDefinition? command)
                ? command!.Name
                : null;

            Aliases.Add(new AliasRow(alias, name));
        }

        Commands.Clear();

        foreach (CommandDefinition command in _runtime.Catalog.Commands
            .OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            Commands.Add(command.Id);
        }

        Selected = null;
        ClearCommand.RaiseCanExecuteChanged();

        Raise(nameof(Summary));
    }

    private bool CanAdd() =>
        !string.IsNullOrWhiteSpace(Phrase) && !string.IsNullOrWhiteSpace(TargetCommand);

    private async Task AddAsync()
    {
        if (!CanAdd())
        {
            return;
        }

        string phrase = Phrase.Trim();
        string normalized = TextNormalizer.Normalize(phrase);

        if (normalized.Length == 0)
        {
            Verdict = "Cette formulation ne contient rien de prononçable.";
            return;
        }

        // Une formulation deja prise rendrait l'une des deux commandes inatteignable : la
        // grammaire ne garde qu'une correspondance par enonce.
        CommandDefinition? owner = _runtime.Catalog.Commands.FirstOrDefault(
            c => c.AllPhrases.Any(p => TextNormalizer.Normalize(p) == normalized));

        if (owner is not null)
        {
            Verdict = string.Equals(owner.Id, TargetCommand, StringComparison.OrdinalIgnoreCase)
                ? $"« {phrase} » est déjà rattachée à cette commande."
                : $"« {phrase} » est déjà employée par « {owner.Name} ».";
            return;
        }

        List<PhraseAlias> aliases = new(_runtime.Aliases)
        {
            new PhraseAlias(TargetCommand!, phrase, Polarity, DateTimeOffset.UtcNow),
        };

        await _runtime.SaveAliasesAsync(aliases).ConfigureAwait(true);

        if (Selected is HesitationRow row)
        {
            _runtime.Understanding.Forget(row.Source);
        }

        await _afterChange().ConfigureAwait(true);

        _log(Localization.Localizer.T("Log.PhraseAdded", phrase, TargetCommand),
            Localization.Localizer.T("Log.PhraseAddedHint"),
            ActivityLevel.Normal);

        Phrase = string.Empty;
        Verdict = "Ajoutée.";
        Refresh();
    }

    private void Ignore()
    {
        if (Selected is not HesitationRow row)
        {
            return;
        }

        _runtime.Understanding.Forget(row.Source);
        _runtime.SaveUnderstanding();
        Refresh();
    }

    private void ClearAll()
    {
        _runtime.Understanding.Clear();
        _runtime.SaveUnderstanding();
        Refresh();
    }

    private async Task RemoveAliasAsync()
    {
        if (SelectedAlias is not AliasRow row)
        {
            return;
        }

        List<PhraseAlias> remaining = _runtime.Aliases
            .Where(a => !ReferenceEquals(a, row.Source))
            .ToList();

        await _runtime.SaveAliasesAsync(remaining).ConfigureAwait(true);
        await _afterChange().ConfigureAwait(true);

        _log(Localization.Localizer.T("Log.PhraseRemoved", row.Phrase), null, ActivityLevel.Muted);
        Refresh();
    }
}
