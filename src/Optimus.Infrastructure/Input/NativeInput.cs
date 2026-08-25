using System.Runtime.InteropServices;

namespace Optimus.Infrastructure.Input;

/// <summary>
/// Déclarations Win32 pour l'injection d'entrées.
///
/// Volontairement interne : rien du reste du projet ne doit connaître ces structures. La
/// frontière publique de la couche est <see cref="SendInputEngine"/>, et sa frontière testable
/// est <see cref="InputTranslator"/>.
/// </summary>
internal static partial class NativeInput
{
    internal const int InputMouse = 0;
    internal const int InputKeyboard = 1;

    internal const uint KeyEventExtendedKey = 0x0001;
    internal const uint KeyEventKeyUp = 0x0002;

    /// <summary>
    /// Injection par scancode plutôt que par code de touche virtuelle.
    ///
    /// Ce drapeau n'est pas optionnel : le spike S0-1 a mesuré qu'une injection en virtual-key
    /// seule arrive dans le Raw Input avec un make code nul, et Star Citizen l'ignore purement
    /// et simplement. Vérifié en jeu (G1 réussi, G2 échoué).
    /// </summary>
    internal const uint KeyEventScanCode = 0x0008;

    internal const uint MouseEventLeftDown = 0x0002;
    internal const uint MouseEventLeftUp = 0x0004;
    internal const uint MouseEventRightDown = 0x0008;
    internal const uint MouseEventRightUp = 0x0010;
    internal const uint MouseEventMiddleDown = 0x0020;
    internal const uint MouseEventMiddleUp = 0x0040;
    internal const uint MouseEventXDown = 0x0080;
    internal const uint MouseEventXUp = 0x0100;
    internal const uint MouseEventWheel = 0x0800;

    internal const uint XButton1 = 0x0001;
    internal const uint XButton2 = 0x0002;

    /// <summary>Un cran de molette.</summary>
    internal const int WheelDelta = 120;

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetCurrentThreadId();

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    internal static partial uint TimeBeginPeriod(uint milliseconds);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    internal static partial uint TimeEndPeriod(uint milliseconds);
}
