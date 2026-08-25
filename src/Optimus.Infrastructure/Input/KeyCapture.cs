using System.Runtime.InteropServices;
using Optimus.Core.Domain.Bindings;

namespace Optimus.Infrastructure.Input;

/// <summary>
/// Attend que le pilote presse une touche, et dit laquelle.
///
/// Lit le <b>scancode</b> plutôt que le code virtuel, et ce n'est pas un détail : sur un clavier
/// AZERTY, la touche marquée « A » porte le code virtuel <c>A</c> mais occupe la position US
/// <c>Q</c>, seule connue de Star Citizen et de l'injection (décision D19). Capturer par le code
/// virtuel ferait enregistrer à Optimus une touche pour en presser une autre — l'assignation
/// paraîtrait juste et ne marcherait pas.
///
/// Le hook bas niveau est enveloppé avec les précautions de D22 : délégué maintenu en vie,
/// désinstallation garantie, et aucune exception laissée traverser le code natif.
/// </summary>
public sealed partial class KeyCapture : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;

    private readonly LowLevelKeyboardProc _callback;
    private readonly TaskCompletionSource<InputSpec?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private nint _hook;
    private bool _disposed;

    public KeyCapture()
    {
        // Conserve la reference : un delegue collecte pendant que le hook est installe fait
        // tomber le processus dans le code natif, sans trace exploitable (vecu au spike S0-1).
        _callback = OnKeyboardEvent;
    }

    /// <summary>Touches qui annulent la capture au lieu d'être assignées.</summary>
    private static readonly HashSet<string> Cancels = new(StringComparer.OrdinalIgnoreCase)
    {
        "ESCAPE",
    };

    /// <summary>
    /// Attend une touche. Retourne <c>null</c> si le pilote appuie sur Échap ou si le délai
    /// expire — l'abandon doit rester possible, une capture qu'on ne peut pas quitter est un piège.
    /// </summary>
    public async Task<InputSpec?> CaptureAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _hook = SetWindowsHookExW(WhKeyboardLl, _callback, GetModuleHandleW(null), 0);

        if (_hook == 0)
        {
            throw new InvalidOperationException(
                $"Installation du hook clavier impossible (erreur {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            using CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timer.CancelAfter(timeout);

            using (timer.Token.Register(() => _completion.TrySetResult(null)))
            {
                // Le hook bas niveau exige une boucle de messages sur le thread qui l'installe.
                while (!_completion.Task.IsCompleted)
                {
                    while (PeekMessageW(out Msg message, 0, 0, 0, 0x0001))
                    {
                        _ = TranslateMessage(ref message);
                        _ = DispatchMessageW(ref message);
                    }

                    await Task.Delay(10, CancellationToken.None).ConfigureAwait(false);
                }
            }

            return await _completion.Task.ConfigureAwait(false);
        }
        finally
        {
            if (_hook != 0)
            {
                _ = UnhookWindowsHookEx(_hook);
                _hook = 0;
            }
        }
    }

    private nint OnKeyboardEvent(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code == HcAction && (wParam == WmKeydown || wParam == WmSyskeydown))
            {
                KbdLlHookStruct data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                bool extended = (data.Flags & 0x01) != 0;
                string? name = ScanCodeMap.NameOf((ushort)data.ScanCode, extended);

                if (name is not null && !IsModifierKey(name))
                {
                    if (Cancels.Contains(name))
                    {
                        _completion.TrySetResult(null);
                    }
                    else
                    {
                        _completion.TrySetResult(new InputSpec(name, ReadModifiers()));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Une exception qui traverse un callback natif tue le processus. Rien ne justifie
            // d'emporter Optimus parce qu'une touche exotique n'a pas su etre nommee.
            _completion.TrySetResult(null);
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>
    /// Modificateurs enfoncés au moment de la frappe.
    ///
    /// Ceux-ci se lisent par code virtuel sans risque : on demande « Ctrl gauche est-il
    /// enfoncé », ce qui ne dépend d'aucune disposition.
    /// </summary>
    private static List<string> ReadModifiers()
    {
        List<string> modifiers = new();

        void Check(int virtualKey, string name)
        {
            if ((GetAsyncKeyState(virtualKey) & 0x8000) != 0)
            {
                modifiers.Add(name);
            }
        }

        Check(0xA0, "LSHIFT");
        Check(0xA1, "RSHIFT");
        Check(0xA2, "LCTRL");
        Check(0xA3, "RCTRL");
        Check(0xA4, "LALT");
        Check(0xA5, "RALT");

        return modifiers;
    }

    private static bool IsModifierKey(string name) => name.ToUpperInvariant() is
        "LSHIFT" or "RSHIFT" or "LCTRL" or "RCTRL" or "LALT" or "RALT";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hook != 0)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = 0;
        }

        _completion.TrySetResult(null);
    }

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetWindowsHookExW(int hookId, LowLevelKeyboardProc callback, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Msg message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref Msg message);
}
