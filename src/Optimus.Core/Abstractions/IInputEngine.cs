using Optimus.Core.Domain.Bindings;

namespace Optimus.Core.Abstractions;

/// <summary>Nature d'un évènement d'entrée émis par le moteur.</summary>
public enum InputEventKind
{
    Down,
    Up,
    Wheel,
}

/// <summary>Évènement d'entrée, tel qu'observé ou simulé.</summary>
/// <param name="Kind">Enfoncement, relâchement ou molette.</param>
/// <param name="Input">Entrée concernée.</param>
/// <param name="OffsetMs">Décalage depuis le début de la séquence, en millisecondes.</param>
public sealed record InputEvent(InputEventKind Kind, InputSpec Input, double OffsetMs)
{
    public override string ToString() =>
        $"{OffsetMs,8:F1} ms  {Kind,-4}  {Input}";
}

/// <summary>
/// Injection d'entrées vers le système.
///
/// Deux implémentations existent : celle qui appuie réellement sur les touches, et celle qui
/// se contente de les consigner. Le reste du moteur ne fait aucune différence entre les deux —
/// c'est ce qui permet d'exécuter tout le pipeline dans une CI sans clavier ni jeu, et de
/// livrer le mode simulation sans code particulier.
/// </summary>
/// <remarks>
/// L'interface est jetable à dessein : un moteur peut détenir des ressources système — la
/// résolution du timer, et surtout des touches enfoncées. Sa libération doit être un point de
/// passage obligé, pas une politesse laissée à l'implémentation.
/// </remarks>
public interface IInputEngine : IDisposable
{
    /// <summary>Vrai si le moteur envoie réellement des entrées au système.</summary>
    bool IsReal { get; }

    /// <summary>Enfonce la touche ou le bouton, modificateurs compris.</summary>
    ValueTask PressAsync(InputSpec input, CancellationToken cancellationToken = default);

    /// <summary>Relâche la touche ou le bouton, modificateurs compris.</summary>
    ValueTask ReleaseAsync(InputSpec input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Relâche toute entrée encore enfoncée.
    ///
    /// Appelé systématiquement en fin de séquence, y compris sur erreur, annulation ou arrêt
    /// d'urgence : une touche restée enfoncée dans un vaisseau en vol ne pardonne pas.
    /// </summary>
    ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default);
}
