using Optimus.Core.Domain.Bindings;

namespace Optimus.Infrastructure.Input;

/// <summary>Nature d'un évènement traduit.</summary>
public enum TranslatedKind
{
    Keyboard,
    MouseButton,
    MouseWheel,
}

/// <summary>
/// Évènement d'entrée prêt à être injecté, exprimé sans dépendance Win32.
///
/// Cette étape intermédiaire n'est pas de la cérémonie : elle rend la traduction
/// <b>vérifiable sans rien envoyer</b>. Les tests contrôlent que « RALT + Y » produit bien
/// 0x38 étendu puis 0x15, dans cet ordre, sans qu'aucune touche ne parte réellement.
/// </summary>
/// <param name="Kind">Clavier, bouton de souris ou molette.</param>
/// <param name="ScanCode">Make code, pour le clavier.</param>
/// <param name="Extended">Préfixe 0xE0.</param>
/// <param name="IsRelease">Relâchement plutôt qu'enfoncement.</param>
/// <param name="MouseButton">Bouton visé, pour la souris.</param>
/// <param name="WheelNotches">Crans de molette, positifs vers le haut.</param>
public sealed record TranslatedInput(
    TranslatedKind Kind,
    ushort ScanCode = 0,
    bool Extended = false,
    bool IsRelease = false,
    MouseButtonKind MouseButton = MouseButtonKind.None,
    int WheelNotches = 0)
{
    public override string ToString() => Kind switch
    {
        TranslatedKind.Keyboard =>
            $"kb 0x{ScanCode:X2}{(Extended ? "+E0" : string.Empty)} {(IsRelease ? "up" : "down")}",
        TranslatedKind.MouseButton =>
            $"mouse {MouseButton} {(IsRelease ? "up" : "down")}",
        _ => $"wheel {WheelNotches:+0;-0}",
    };
}

/// <summary>Boutons de souris pris en charge.</summary>
public enum MouseButtonKind
{
    None,
    Left,
    Right,
    Middle,
    X1,
    X2,
}

/// <summary>
/// Traduit une <see cref="InputSpec"/> du domaine en évènements injectables.
///
/// Deux règles gouvernent l'ordre des évènements, et elles comptent autant que les codes :
/// les modificateurs s'enfoncent <b>avant</b> la touche et se relâchent <b>après</b>, dans
/// l'ordre inverse. Un jeu qui lit l'état du clavier au moment de l'appui ne verra sinon
/// pas la combinaison.
/// </summary>
public static class InputTranslator
{
    /// <summary>Évènements d'enfoncement : modificateurs puis touche.</summary>
    public static IReadOnlyList<TranslatedInput> BuildPress(InputSpec input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<TranslatedInput> events = new();

        // La molette n'a pas d'état : un cran est un évènement unique, sans relâchement.
        if (TryGetWheel(input, out int notches))
        {
            events.Add(new TranslatedInput(TranslatedKind.MouseWheel, WheelNotches: notches));
            return events;
        }

        foreach (ScanCode modifier in ResolveModifiers(input))
        {
            events.Add(Keyboard(modifier, release: false));
        }

        events.Add(MainDown(input));
        return events;
    }

    /// <summary>Évènements de relâchement : touche puis modificateurs, en ordre inverse.</summary>
    public static IReadOnlyList<TranslatedInput> BuildRelease(InputSpec input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<TranslatedInput> events = new();

        if (TryGetWheel(input, out _))
        {
            return events;
        }

        events.Add(MainUp(input));

        foreach (ScanCode modifier in ResolveModifiers(input).Reverse())
        {
            events.Add(Keyboard(modifier, release: true));
        }

        return events;
    }

    /// <summary>Vrai si l'entrée peut être injectée : touche connue ou bouton reconnu.</summary>
    public static bool CanTranslate(InputSpec input, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (TryGetWheel(input, out _))
        {
            reason = null;
            return true;
        }

        if (input.Device == InputDevice.Mouse)
        {
            if (ParseMouseButton(input.Key) == MouseButtonKind.None)
            {
                reason = $"Bouton de souris inconnu : « {input.Key} ».";
                return false;
            }

            reason = null;
            return true;
        }

        if (input.Device == InputDevice.Gamepad)
        {
            reason = "Les manettes ne sont pas prises en charge par le MVP.";
            return false;
        }

        if (!ScanCodeMap.TryGet(input.Key, out _))
        {
            reason = $"Touche inconnue : « {input.Key} ».";
            return false;
        }

        foreach (string modifier in input.Modifiers)
        {
            if (!ScanCodeMap.TryGet(modifier, out _))
            {
                reason = $"Modificateur inconnu : « {modifier} ».";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static TranslatedInput MainDown(InputSpec input) => BuildMain(input, release: false);

    private static TranslatedInput MainUp(InputSpec input) => BuildMain(input, release: true);

    private static TranslatedInput BuildMain(InputSpec input, bool release)
    {
        if (input.Device == InputDevice.Mouse)
        {
            MouseButtonKind button = ParseMouseButton(input.Key);
            if (button == MouseButtonKind.None)
            {
                throw new KeyNotFoundException($"Bouton de souris inconnu : « {input.Key} ».");
            }

            return new TranslatedInput(TranslatedKind.MouseButton, IsRelease: release, MouseButton: button);
        }

        ScanCode scanCode = ScanCodeMap.Get(input.Key);
        return Keyboard(scanCode, release);
    }

    private static TranslatedInput Keyboard(ScanCode scanCode, bool release) =>
        new(TranslatedKind.Keyboard, scanCode.Value, scanCode.Extended, release);

    private static IReadOnlyList<ScanCode> ResolveModifiers(InputSpec input)
    {
        if (input.Modifiers.Count == 0)
        {
            return Array.Empty<ScanCode>();
        }

        List<ScanCode> resolved = new(input.Modifiers.Count);
        foreach (string modifier in input.Modifiers)
        {
            resolved.Add(ScanCodeMap.Get(modifier));
        }

        return resolved;
    }

    private static bool TryGetWheel(InputSpec input, out int notches)
    {
        switch (input.Key.ToUpperInvariant())
        {
            case "WHEEL_UP":
                notches = 1;
                return true;

            case "WHEEL_DOWN":
                notches = -1;
                return true;

            default:
                notches = 0;
                return false;
        }
    }

    private static MouseButtonKind ParseMouseButton(string key) => key.ToUpperInvariant() switch
    {
        "MOUSE1" or "LEFT" or "LBUTTON" => MouseButtonKind.Left,
        "MOUSE2" or "RIGHT" or "RBUTTON" => MouseButtonKind.Right,
        "MOUSE3" or "MIDDLE" or "MBUTTON" => MouseButtonKind.Middle,
        "MOUSE4" or "X1" => MouseButtonKind.X1,
        "MOUSE5" or "X2" => MouseButtonKind.X2,
        _ => MouseButtonKind.None,
    };
}
