using System.Windows;
using System.Windows.Interop;
using Optimus.Core.Domain.Bindings;
using Optimus.Infrastructure.Input;

namespace Optimus.App.Input;

/// <summary>
/// Capture la touche pressée pendant qu'une fenêtre a le focus.
///
/// Lit le <b>scancode</b> dans le message Win32 lui-même — bits 16 à 23 de <c>lParam</c>, bit 24
/// pour le préfixe étendu — et non <c>KeyEventArgs.Key</c>, que WPF a déjà traduit selon la
/// disposition active. Sur AZERTY, la touche marquée « A » y arriverait comme <c>Key.A</c> alors
/// qu'elle occupe la position US <c>Q</c>, seule connue de Star Citizen et de l'injection (D19).
/// Optimus enregistrerait une touche pour en presser une autre.
///
/// Le crochet est posé sur notre propre fenêtre, jamais sur le système : il ne voit que ce qui
/// nous est adressé. C'est la même discipline que la version console, pour la même raison —
/// un crochet global est la signature d'un enregistreur de frappe, et Smart App Control l'a
/// fait savoir (R16, D36).
/// </summary>
public sealed class WindowKeyCapture : IDisposable
{
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private readonly HwndSource _source;
    private readonly TaskCompletionSource<InputSpec?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _disposed;

    private WindowKeyCapture(HwndSource source)
    {
        _source = source;
        _source.AddHook(OnMessage);
    }

    /// <summary>
    /// Attend une touche. Retourne <c>null</c> sur Échap ou à l'expiration du délai : une
    /// capture dont on ne peut pas sortir est un piège.
    /// </summary>
    public static async Task<InputSpec?> CaptureAsync(Window window, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (PresentationSource.FromVisual(window) is not HwndSource source)
        {
            throw new InvalidOperationException("La fenêtre n'est pas encore affichée.");
        }

        using WindowKeyCapture capture = new(source);
        using CancellationTokenSource timer = new(timeout);
        using CancellationTokenRegistration registration =
            timer.Token.Register(() => capture._completion.TrySetResult(null));

        return await capture._completion.Task.ConfigureAwait(true);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is not (WmKeyDown or WmSysKeyDown))
        {
            return nint.Zero;
        }

        // lParam : bits 16-23 le scancode, bit 24 le préfixe étendu.
        long bits = lParam.ToInt64();
        ushort scanCode = (ushort)((bits >> 16) & 0xFF);
        bool extended = ((bits >> 24) & 0x01) != 0;

        string? name = ScanCodeMap.NameOf(scanCode, extended);

        if (name is null || IsModifier(name))
        {
            return nint.Zero;
        }

        // La touche est consommée : sans cela, Alt ouvrirait le menu système et Tab
        // déplacerait le focus au milieu de la capture.
        handled = true;

        _completion.TrySetResult(
            name.Equals("ESCAPE", StringComparison.OrdinalIgnoreCase)
                ? null
                : new InputSpec(name, ReadModifiers()));

        return nint.Zero;
    }

    /// <summary>
    /// Modificateurs enfoncés. Lus par code virtuel sans risque : « Ctrl gauche est-il
    /// enfoncé » ne dépend d'aucune disposition clavier.
    /// </summary>
    private static List<string> ReadModifiers()
    {
        List<string> modifiers = new();

        void Check(System.Windows.Input.Key key, string name)
        {
            if (System.Windows.Input.Keyboard.IsKeyDown(key))
            {
                modifiers.Add(name);
            }
        }

        Check(System.Windows.Input.Key.LeftShift, "LSHIFT");
        Check(System.Windows.Input.Key.RightShift, "RSHIFT");
        Check(System.Windows.Input.Key.LeftCtrl, "LCTRL");
        Check(System.Windows.Input.Key.RightCtrl, "RCTRL");
        Check(System.Windows.Input.Key.LeftAlt, "LALT");
        Check(System.Windows.Input.Key.RightAlt, "RALT");

        return modifiers;
    }

    private static bool IsModifier(string name) => name.ToUpperInvariant() is
        "LSHIFT" or "RSHIFT" or "LCTRL" or "RCTRL" or "LALT" or "RALT";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(OnMessage);
        _completion.TrySetResult(null);
    }
}
