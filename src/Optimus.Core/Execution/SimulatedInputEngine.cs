using System.Diagnostics;
using Optimus.Core.Abstractions;
using Optimus.Core.Domain.Bindings;

namespace Optimus.Core.Execution;

/// <summary>
/// Moteur d'entrée qui n'appuie sur rien et consigne tout.
///
/// Il vit dans le cœur, et non dans la couche d'infrastructure, parce qu'il ne touche aucune
/// API système : c'est du domaine pur. Il sert deux usages qui n'en font qu'un — le mode
/// simulation offert à l'utilisateur (RF-E08) et l'exécution du pipeline complet en intégration
/// continue, sans clavier ni jeu.
/// </summary>
public sealed class SimulatedInputEngine : IInputEngine
{
    private readonly List<InputEvent> _events = new();
    private readonly List<InputSpec> _pressed = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public bool IsReal => false;

    /// <summary>Évènements consignés depuis la dernière remise à zéro.</summary>
    public IReadOnlyList<InputEvent> Events => _events;

    /// <summary>Entrées actuellement enfoncées. Doit être vide en fin de séquence.</summary>
    public IReadOnlyList<InputSpec> StillPressed => _pressed;

    public ValueTask PressAsync(InputSpec input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        _events.Add(new InputEvent(InputEventKind.Down, input, _clock.Elapsed.TotalMilliseconds));
        _pressed.Add(input);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(InputSpec input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // Le relâchement doit aboutir même sur annulation : c'est précisément le moment où
        // il compte le plus.
        _events.Add(new InputEvent(InputEventKind.Up, input, _clock.Elapsed.TotalMilliseconds));

        int index = _pressed.FindLastIndex(p => p.Key == input.Key && p.Device == input.Device);
        if (index >= 0)
        {
            _pressed.RemoveAt(index);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        for (int i = _pressed.Count - 1; i >= 0; i--)
        {
            _events.Add(new InputEvent(InputEventKind.Up, _pressed[i], _clock.Elapsed.TotalMilliseconds));
        }

        _pressed.Clear();
        return ValueTask.CompletedTask;
    }

    /// <summary>Vide le journal et l'état, entre deux commandes ou deux tests.</summary>
    public void Reset()
    {
        _events.Clear();
        _pressed.Clear();
        _clock.Restart();
    }

    /// <summary>
    /// Aucune ressource système à libérer — mais on relâche tout de même l'état, pour que le
    /// contrat soit honoré à l'identique par les deux moteurs.
    /// </summary>
    public void Dispose() => _pressed.Clear();

    /// <summary>Rendu lisible du journal, tel qu'affiché en mode simulation.</summary>
    public string Transcript() =>
        _events.Count == 0
            ? "(aucune entrée)"
            : string.Join(Environment.NewLine, _events.Select(e => e.ToString()));
}
