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
/// La lecture passe par le <b>tampon d'entrée de la console</b>, dont les enregistrements
/// portent déjà <c>wVirtualScanCode</c>. La première version employait un hook bas niveau
/// <c>WH_KEYBOARD_LL</c> : il fonctionnait, mais interceptait les frappes de <i>toutes</i> les
/// applications pour lire une seule touche destinée à celle-ci — disproportionné, et c'est la
/// signature même d'un enregistreur de frappe. La première publication qui en contenait un s'est
/// fait bloquer par Smart App Control (risque R16), là où les précédentes passaient.
///
/// Le tampon de console ne reçoit que ce qui est adressé à Optimus. Rien à désinstaller, rien à
/// laisser traîner si le processus meurt : le périmètre est le bon, et la lecture est exacte.
/// </summary>
public sealed partial class KeyCapture : IDisposable
{
    private const int StdInputHandle = -10;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const ushort KeyEvent = 0x0001;
    private const uint EnhancedKey = 0x0100;
    private const uint WaitObject0 = 0;

    // Les modificateurs sont annoncés par l'enregistrement lui-même : nul besoin d'interroger
    // l'état global du clavier.
    private const uint RightAltPressed = 0x0001;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightCtrlPressed = 0x0004;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint ShiftPressed = 0x0010;

    private readonly nint _input = GetStdHandle(StdInputHandle);
    private readonly uint _previousMode;
    private readonly bool _modeChanged;
    private readonly bool _isConsole;
    private bool _disposed;

    public KeyCapture()
    {
        if (_input == 0 || _input == -1)
        {
            return;
        }

        // Entree redirigee : ReadConsoleInput echouera a chaque tour. Le savoir tout de suite
        // evite de tourner a vide jusqu'a l'expiration du delai, et permet de le dire.
        _isConsole = GetConsoleMode(_input, out _previousMode);
        if (!_isConsole)
        {
            return;
        }

        // Sans ces deux drapeaux, la console attend une ligne entière et fait l'écho de la
        // frappe. On garde ENABLE_PROCESSED_INPUT : Ctrl+C doit rester une sortie de secours.
        _modeChanged = SetConsoleMode(_input, _previousMode & ~(EnableLineInput | EnableEchoInput));
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
    public Task<InputSpec?> CaptureAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_input == 0 || _input == -1 || !_isConsole)
        {
            throw new InvalidOperationException(
                "Key capture needs an interactive terminal: input is redirected. "
                + "Lancez « --bind » directement dans une console, sans tube ni redirection.");
        }

        // La lecture est bloquante : elle appartient à un thread dédié, jamais au pool.
        return Task.Factory.StartNew(
            () => Capture(timeout, cancellationToken),
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private InputSpec? Capture(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Ce que le pilote a tapé avant d'arriver ici ne le concerne pas.
        _ = FlushConsoleInputBuffer(_input);

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            // Un réveil régulier plutôt qu'une attente unique : l'annulation doit être entendue.
            uint slice = (uint)Math.Min(200, Math.Max(1, remaining.TotalMilliseconds));

            if (WaitForSingleObject(_input, slice) != WaitObject0)
            {
                continue;
            }

            if (!ReadConsoleInputW(_input, out InputRecord record, 1, out uint read) || read == 0)
            {
                continue;
            }

            if (record.EventType != KeyEvent || record.KeyEvent.KeyDown == 0)
            {
                continue;
            }

            ushort scanCode = record.KeyEvent.VirtualScanCode;
            if (scanCode == 0)
            {
                continue;
            }

            bool extended = (record.KeyEvent.ControlKeyState & EnhancedKey) != 0;
            string? name = ScanCodeMap.NameOf(scanCode, extended);

            if (name is null || IsModifierKey(name))
            {
                continue;
            }

            return Cancels.Contains(name)
                ? null
                : new InputSpec(name, ReadModifiers(record.KeyEvent.ControlKeyState));
        }

        return null;
    }

    private static List<string> ReadModifiers(uint state)
    {
        List<string> modifiers = new();

        if ((state & LeftAltPressed) != 0)
        {
            modifiers.Add("LALT");
        }

        if ((state & RightAltPressed) != 0)
        {
            modifiers.Add("RALT");
        }

        if ((state & LeftCtrlPressed) != 0)
        {
            modifiers.Add("LCTRL");
        }

        if ((state & RightCtrlPressed) != 0)
        {
            modifiers.Add("RCTRL");
        }

        // La console ne distingue pas les deux Maj. On retient la gauche, forme que Star Citizen
        // écrit aussi bien : mieux vaut une approximation nommée qu'un silence.
        if ((state & ShiftPressed) != 0)
        {
            modifiers.Add("LSHIFT");
        }

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

        if (_modeChanged)
        {
            _ = SetConsoleMode(_input, _previousMode);
        }
    }

    /// <summary>
    /// Reflet exact de <c>KEY_EVENT_RECORD</c>.
    ///
    /// Champs volontairement bruts — <c>int</c> pour un booleen, <c>ushort</c> pour un caractere —
    /// afin que la structure reste blittable : le generateur de <c>LibraryImport</c> exige de
    /// pouvoir la passer sans marshaling, et c'est aussi ce qu'il y a de plus rapide.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyEventRecord
    {
        public int KeyDown;
        public ushort RepeatCount;
        public ushort VirtualKeyCode;
        public ushort VirtualScanCode;
        public ushort Character;
        public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)]
        public ushort EventType;

        [FieldOffset(4)]
        public KeyEventRecord KeyEvent;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushConsoleInputBuffer(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadConsoleInputW(nint handle, out InputRecord record, uint length, out uint read);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);
}
