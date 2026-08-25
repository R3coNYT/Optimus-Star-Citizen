namespace Optimus.Core.Domain.Bindings;

/// <summary>Périphérique portant l'entrée.</summary>
public enum InputDevice
{
    Keyboard,
    Mouse,

    /// <summary>Hors périmètre du MVP, présent pour que l'import n'ait rien à inventer.</summary>
    Gamepad,
}

/// <summary>
/// Mode d'activation d'une entrée.
///
/// Ces modes proviennent de Star Citizen lui-même : <c>defaultProfile.xml</c> déclare ses
/// <c>ActivationModes</c> avec leurs seuils, que l'importeur traduit ici. C'est pourquoi
/// <see cref="InputSpec.HoldMs"/> n'est pas une constante globale : un
/// <c>delayed_press_medium</c> exige un maintien de 500 ms, là où un <c>tap</c> doit être
/// relâché en moins de 250 ms.
/// </summary>
public enum InputMode
{
    /// <summary>Appui bref.</summary>
    Tap,

    /// <summary>Appui maintenu pendant <see cref="InputSpec.HoldMs"/>.</summary>
    Hold,

    /// <summary>Deux appuis brefs séparés d'un court intervalle.</summary>
    DoubleTap,

    /// <summary>Enfoncement seul, sans relâchement automatique.</summary>
    Press,

    /// <summary>Relâchement seul.</summary>
    Release,
}

/// <summary>
/// Valeurs par défaut du moteur d'entrée.
///
/// Déclarées hors de <see cref="InputSpec"/> parce que les valeurs par défaut des paramètres
/// d'un record positionnel ne peuvent pas référencer les membres du type qu'elles définissent.
/// </summary>
public static class InputDefaults
{
    /// <summary>
    /// Durée d'appui par défaut. Mesurée au spike S0-1 : Star Citizen accepte 16 ms,
    /// 45 ms offre donc une marge de sécurité de l'ordre de trois sans peser sur la latence.
    /// </summary>
    public const int HoldMs = 45;

    /// <summary>Intervalle par défaut entre deux répétitions.</summary>
    public const int IntervalMs = 60;

    /// <summary>Écart entre les deux appuis d'un double appui.</summary>
    public const int DoubleTapGapMs = 80;
}

/// <summary>
/// Description d'une entrée physique, indépendante de toute API système.
///
/// C'est la frontière du domaine : au-delà, l'<c>IInputEngine</c> traduit en scancodes.
/// Le nom de touche suit les positions clavier US, comme Star Citizen les nomme
/// (<c>kb1_l</c> → <c>"L"</c>) — jamais la disposition Windows active, qui donnerait
/// une touche différente sur un clavier AZERTY.
/// </summary>
/// <param name="Key">Nom canonique de la touche ou du bouton (<c>L</c>, <c>F5</c>, <c>MOUSE2</c>, <c>WHEEL_UP</c>).</param>
/// <param name="Modifiers">Modificateurs, triés et sans doublon (<c>LALT</c>, <c>RALT</c>, <c>LSHIFT</c>…).</param>
/// <param name="Device">Périphérique concerné.</param>
/// <param name="Mode">Mode d'activation.</param>
/// <param name="HoldMs">Durée de maintien, en millisecondes. Ignorée hors mode <see cref="InputMode.Hold"/>.</param>
/// <param name="Repeat">Nombre de répétitions de l'entrée complète.</param>
/// <param name="IntervalMs">Intervalle entre deux répétitions.</param>
public sealed record InputSpec(
    string Key,
    IReadOnlyList<string> Modifiers,
    InputDevice Device = InputDevice.Keyboard,
    InputMode Mode = InputMode.Tap,
    int HoldMs = InputDefaults.HoldMs,
    int Repeat = 1,
    int IntervalMs = InputDefaults.IntervalMs)
{
    /// <inheritdoc cref="InputDefaults.HoldMs"/>
    public const int DefaultHoldMs = InputDefaults.HoldMs;

    /// <inheritdoc cref="InputDefaults.IntervalMs"/>
    public const int DefaultIntervalMs = InputDefaults.IntervalMs;

    /// <summary>Entrée sans modificateur.</summary>
    public static InputSpec Simple(string key, InputMode mode = InputMode.Tap) =>
        new(key, Array.Empty<string>(), InputDevice.Keyboard, mode);

    /// <summary>Représentation lisible, telle qu'affichée dans l'interface et les journaux.</summary>
    public override string ToString()
    {
        string combination = Modifiers.Count == 0
            ? Key
            : string.Join(" + ", Modifiers.Append(Key));

        return Mode switch
        {
            InputMode.Hold => $"{combination} (maintien {HoldMs} ms)",
            InputMode.DoubleTap => $"{combination} (double appui)",
            InputMode.Press => $"{combination} (enfoncé)",
            InputMode.Release => $"{combination} (relâché)",
            _ => Repeat > 1 ? $"{combination} ×{Repeat}" : combination,
        };
    }
}
