using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Optimus.Core.Abstractions;
using Optimus.Core.Domain.Bindings;

namespace Optimus.Infrastructure.Input;

/// <summary>
/// Moteur d'injection réel, par <c>SendInput</c> en scancodes.
///
/// Validé en jeu au spike S0-1 : Star Citizen réagit aux entrées produites ici, en utilisateur
/// standard et sans élévation. Trois choix y sont ancrés, chacun appuyé sur une mesure :
///
/// <list type="bullet">
///   <item>scancode obligatoire — une injection en virtual-key seule est ignorée par le jeu ;</item>
///   <item>table de scancodes fixe en positions US — <c>MapVirtualKey</c> ment sur clavier AZERTY ;</item>
///   <item>résolution du timer à 1 ms pendant l'exécution — sinon les maintiens courts sont faux.</item>
/// </list>
///
/// La classe suit les touches enfoncées pour pouvoir tout relâcher : une touche restée en
/// position basse dans un vaisseau en vol est le pire défaut possible pour cet outil.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SendInputEngine : IInputEngine, IDisposable
{
    /// <summary>
    /// Signature « OPT1 » déposée dans <c>dwExtraInfo</c> de chaque évènement.
    ///
    /// Elle permet à Optimus de reconnaître ses propres injections dans son hook clavier et de
    /// ne pas se déclencher lui-même — le piège classique des outils d'automatisation.
    /// </summary>
    public static readonly nint Signature = 0x4F505431;

    private static readonly int InputSize = Marshal.SizeOf<NativeInput.Input>();

    private readonly List<InputSpec> _pressed = new();

    // System.Threading.Lock n'existe qu'à partir de .NET 9 ; sur net8.0 on garde le verrou
    // classique sur un objet dédié.
    private readonly object _sync = new();
    private readonly bool _ownsTimerResolution;
    private bool _disposed;

    /// <param name="raiseTimerResolution">
    /// Passe la résolution du timer système à 1 ms. Sans cela, la granularité est d'environ
    /// 15 ms et un maintien de 45 ms en vaut 60 : mesuré au spike S0-1, l'écart tombe à moins
    /// de 1,4 ms avec.
    /// </param>
    public SendInputEngine(bool raiseTimerResolution = true)
    {
        if (raiseTimerResolution)
        {
            _ownsTimerResolution = NativeInput.TimeBeginPeriod(1) == 0;
        }
    }

    public bool IsReal => true;

    /// <summary>Entrées actuellement enfoncées par ce moteur.</summary>
    public IReadOnlyList<InputSpec> Pressed
    {
        get
        {
            lock (_sync)
            {
                return _pressed.ToList();
            }
        }
    }

    public ValueTask PressAsync(InputSpec input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        Send(InputTranslator.BuildPress(input));

        // La molette n'a pas d'état bas : rien à relâcher plus tard.
        if (!IsWheel(input))
        {
            lock (_sync)
            {
                _pressed.Add(input);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAsync(InputSpec input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Volontairement insensible à l'annulation : refuser de relâcher une touche parce
        // qu'une annulation vient d'arriver serait exactement le contraire de ce qu'il faut.
        Send(InputTranslator.BuildRelease(input));

        lock (_sync)
        {
            int index = _pressed.FindLastIndex(p => p.Key == input.Key && p.Device == input.Device);
            if (index >= 0)
            {
                _pressed.RemoveAt(index);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        List<InputSpec> toRelease;

        lock (_sync)
        {
            toRelease = _pressed.ToList();
            _pressed.Clear();
        }

        // Ordre inverse de l'enfoncement, pour relâcher les modificateurs en dernier.
        for (int i = toRelease.Count - 1; i >= 0; i--)
        {
            Send(InputTranslator.BuildRelease(toRelease[i]));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Vérifie qu'une entrée est injectable, sans rien envoyer.</summary>
    public static bool CanSend(InputSpec input, out string? reason) =>
        InputTranslator.CanTranslate(input, out reason);

    private static bool IsWheel(InputSpec input) =>
        input.Key.Equals("WHEEL_UP", StringComparison.OrdinalIgnoreCase) ||
        input.Key.Equals("WHEEL_DOWN", StringComparison.OrdinalIgnoreCase);

    private static void Send(IReadOnlyList<TranslatedInput> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        NativeInput.Input[] native = new NativeInput.Input[events.Count];

        for (int i = 0; i < events.Count; i++)
        {
            native[i] = ToNative(events[i]);
        }

        uint sent = NativeInput.SendInput((uint)native.Length, native, InputSize);

        if (sent != native.Length)
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput n'a injecté que {sent} évènement(s) sur {native.Length} (code {error}). " +
                "Cause la plus probable : la fenêtre au premier plan est élevée alors qu'Optimus ne l'est pas.");
        }
    }

    private static NativeInput.Input ToNative(TranslatedInput translated)
    {
        NativeInput.Input native = default;

        switch (translated.Kind)
        {
            case TranslatedKind.Keyboard:
                {
                    uint flags = NativeInput.KeyEventScanCode;
                    if (translated.Extended)
                    {
                        flags |= NativeInput.KeyEventExtendedKey;
                    }

                    if (translated.IsRelease)
                    {
                        flags |= NativeInput.KeyEventKeyUp;
                    }

                    native.Type = NativeInput.InputKeyboard;
                    native.Union.Keyboard = new NativeInput.KeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = translated.ScanCode,
                        Flags = flags,
                        Time = 0,
                        ExtraInfo = Signature,
                    };
                    break;
                }

            case TranslatedKind.MouseButton:
                {
                    (uint flags, uint data) = MouseFlags(translated.MouseButton, translated.IsRelease);

                    native.Type = NativeInput.InputMouse;
                    native.Union.Mouse = new NativeInput.MouseInput
                    {
                        MouseData = data,
                        Flags = flags,
                        ExtraInfo = Signature,
                    };
                    break;
                }

            case TranslatedKind.MouseWheel:
            default:
                native.Type = NativeInput.InputMouse;
                native.Union.Mouse = new NativeInput.MouseInput
                {
                    MouseData = unchecked((uint)(translated.WheelNotches * NativeInput.WheelDelta)),
                    Flags = NativeInput.MouseEventWheel,
                    ExtraInfo = Signature,
                };
                break;
        }

        return native;
    }

    private static (uint Flags, uint Data) MouseFlags(MouseButtonKind button, bool release) => button switch
    {
        MouseButtonKind.Left => (release ? NativeInput.MouseEventLeftUp : NativeInput.MouseEventLeftDown, 0u),
        MouseButtonKind.Right => (release ? NativeInput.MouseEventRightUp : NativeInput.MouseEventRightDown, 0u),
        MouseButtonKind.Middle => (release ? NativeInput.MouseEventMiddleUp : NativeInput.MouseEventMiddleDown, 0u),
        MouseButtonKind.X1 => (release ? NativeInput.MouseEventXUp : NativeInput.MouseEventXDown, NativeInput.XButton1),
        MouseButtonKind.X2 => (release ? NativeInput.MouseEventXUp : NativeInput.MouseEventXDown, NativeInput.XButton2),
        _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Bouton de souris non pris en charge."),
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            ReleaseAllAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            // Un échec de relâchement pendant la libération ne doit pas masquer la cause initiale.
        }

        if (_ownsTimerResolution)
        {
            NativeInput.TimeEndPeriod(1);
        }
    }
}
