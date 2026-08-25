using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Optimus.Spike
{
    /// <summary>Un évènement d'entrée observé par une sonde.</summary>
    public sealed class ObservedEvent
    {
        /// <summary>"hook" (WH_KEYBOARD_LL) ou "rawinput" (WM_INPUT).</summary>
        public string Source;
        public DateTime TimestampUtc;

        /// <summary>
        /// Horodatage haute résolution (Stopwatch.GetTimestamp). DateTime.UtcNow a une
        /// granularité d'environ 15 ms sous Windows : inutilisable pour mesurer un maintien
        /// de 16 ms.
        /// </summary>
        public long Ticks;

        public ushort ScanCode;
        public ushort VirtualKey;
        public bool Extended;
        public bool KeyUp;

        /// <summary>Évènement marqué comme injecté par Windows (LLKHF_INJECTED). Hook uniquement.</summary>
        public bool Injected;

        /// <summary>Injecté par un processus d'intégrité inférieure (LLKHF_LOWER_IL_INJECTED).</summary>
        public bool LowerIntegrityInjected;

        /// <summary>Notre signature dwExtraInfo a été retrouvée : l'évènement vient de ce spike.</summary>
        public bool Ours;

        public override string ToString()
        {
            return string.Format("{0,-9} sc=0x{1:X2}{2} vk=0x{3:X2} {4}{5}{6}",
                Source, ScanCode, Extended ? "+E0" : "   ", VirtualKey,
                KeyUp ? "UP  " : "DOWN",
                Injected ? " [injected]" : "",
                Ours ? " [ours]" : "");
        }
    }

    /// <summary>
    /// Sondes d'observation de l'entrée, hébergées sur un thread dédié avec sa propre boucle de
    /// messages :
    ///
    ///  • <b>Hook bas niveau</b> (WH_KEYBOARD_LL) : voit tous les évènements clavier de la session
    ///    et expose le drapeau LLKHF_INJECTED — c'est précisément ce drapeau que les anti-triches
    ///    inspectent, donc l'information la plus importante du spike.
    ///
    ///  • <b>Raw Input</b> (WM_INPUT, RIDEV_INPUTSINK) : c'est le chemin d'entrée qu'utilisent les
    ///    moteurs de jeu (dont CryEngine/Star Citizen). Si nos scancodes y apparaissent
    ///    correctement, un jeu lisant le Raw Input les verra tels quels.
    /// </summary>
    public sealed class InputProbe : IDisposable
    {
        /// <summary>
        /// Nom de classe de fenêtre **unique par instance**.
        ///
        /// Une classe de fenêtre est enregistrée au niveau du *processus* et survit à la
        /// destruction de la fenêtre. Avec un nom fixe, un second lancement dans le même
        /// processus (typiquement : relancer le script dans la même console PowerShell)
        /// réutilisait la classe précédente, dont le lpfnWndProc pointe vers un délégué que le
        /// GC a pu collecter — d'où une NullReferenceException levée depuis du code natif, qui
        /// tue le processus hôte. Un nom unique par instance supprime la classe de possibilités.
        /// </summary>
        private readonly string _windowClassName =
            "OptimusSpikeRawInputSink_" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Les délégués passés à Win32 doivent survivre aussi longtemps que Windows peut les
        /// rappeler. On les garde référencés pour toute la durée du processus : c'est un coût
        /// dérisoire face à un crash natif.
        /// </summary>
        private static readonly List<object> NativeCallbackKeepAlive = new List<object>();

        private readonly object _sync = new object();
        private readonly List<ObservedEvent> _events = new List<ObservedEvent>();

        private Thread _thread;
        private uint _threadId;
        private IntPtr _hookHandle = IntPtr.Zero;
        private IntPtr _windowHandle = IntPtr.Zero;
        private IntPtr _moduleHandle = IntPtr.Zero;
        private bool _classRegistered;

        // Les délégués doivent rester référencés : sinon le GC les collecte et Windows
        // rappelle un pointeur invalide.
        private Interop.HookProc _hookProc;
        private Interop.WndProcDelegate _wndProc;

        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private volatile bool _stopping;

        public bool HookInstalled { get; private set; }
        public bool RawInputRegistered { get; private set; }
        public string HookError { get; private set; }
        public string RawInputError { get; private set; }

        /// <summary>Passe à vrai si l'utilisateur appuie réellement sur Échap (arrêt d'urgence).</summary>
        public volatile bool AbortRequested;

        // ------------------------------------------------------ Raccourci global (S0-3)

        /// <summary>Code de touche virtuelle à enregistrer via RegisterHotKey (0 = aucun).</summary>
        public ushort HotkeyVirtualKey;

        /// <summary>Modificateurs MOD_* du raccourci global.</summary>
        public uint HotkeyModifiers;

        public bool HotkeyRegistered { get; private set; }
        public string HotkeyError { get; private set; }

        /// <summary>Horodatages haute résolution des WM_HOTKEY reçus.</summary>
        private readonly List<long> _hotkeyHits = new List<long>();

        private const int HotkeyId = 0x4F50; // "OP"

        public int HotkeyHitCount
        {
            get { lock (_sync) { return _hotkeyHits.Count; } }
        }

        public void Start()
        {
            _thread = new Thread(PumpThread);
            _thread.IsBackground = true;
            _thread.Name = "OptimusSpikeProbe";
            _thread.Start();
            _ready.WaitOne(3000);
        }

        private void PumpThread()
        {
            // Ce thread est un thread de fond : une exception non gérée ici tuerait le processus
            // hôte (PowerShell compris). Une sonde qui échoue doit dégrader, jamais crasher.
            try
            {
                _threadId = Interop.GetCurrentThreadId();

                try
                {
                    InstallHook();
                    CreateSinkWindow();
                    RegisterGlobalHotkey();
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(RawInputError)) RawInputError = ex.Message;
                }
                finally
                {
                    // Toujours débloquer Start(), même si l'installation a échoué.
                    _ready.Set();
                }

                Interop.MSG msg;
                while (!_stopping && Interop.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    // RegisterHotKey poste WM_HOTKEY dans la file du THREAD (hWnd nul) :
                    // le message n'est associé à aucune fenêtre, il faut donc l'intercepter
                    // ici et non dans une WndProc.
                    if (msg.message == Interop.WM_HOTKEY && msg.wParam.ToInt32() == HotkeyId)
                    {
                        lock (_sync) { _hotkeyHits.Add(System.Diagnostics.Stopwatch.GetTimestamp()); }
                    }

                    Interop.TranslateMessage(ref msg);
                    Interop.DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(RawInputError)) RawInputError = "boucle de messages : " + ex.Message;
                _ready.Set();
            }
            finally
            {
                try { Cleanup(); }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ Hook

        private void InstallHook()
        {
            try
            {
                _hookProc = HookCallback;
                lock (NativeCallbackKeepAlive) { NativeCallbackKeepAlive.Add(_hookProc); }
                IntPtr module = Interop.GetModuleHandle(null);
                _hookHandle = Interop.SetWindowsHookEx(Interop.WH_KEYBOARD_LL, _hookProc, module, 0);
                if (_hookHandle == IntPtr.Zero)
                {
                    HookError = "SetWindowsHookEx a échoué (code " + Marshal.GetLastWin32Error() + ")";
                    return;
                }
                HookInstalled = true;
            }
            catch (Exception ex)
            {
                HookError = ex.Message;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode == Interop.HC_ACTION)
            {
                try
                {
                    Interop.KBDLLHOOKSTRUCT data = (Interop.KBDLLHOOKSTRUCT)
                        Marshal.PtrToStructure(lParam, typeof(Interop.KBDLLHOOKSTRUCT));

                    int message = wParam.ToInt32();
                    bool keyUp = message == Interop.WM_KEYUP || message == Interop.WM_SYSKEYUP;

                    ObservedEvent evt = new ObservedEvent();
                    evt.Source = "hook";
                    evt.TimestampUtc = DateTime.UtcNow;
                    evt.Ticks = System.Diagnostics.Stopwatch.GetTimestamp();
                    evt.ScanCode = (ushort)data.scanCode;
                    evt.VirtualKey = (ushort)data.vkCode;
                    evt.Extended = (data.flags & Interop.LLKHF_EXTENDED) != 0;
                    evt.KeyUp = keyUp;
                    evt.Injected = (data.flags & Interop.LLKHF_INJECTED) != 0;
                    evt.LowerIntegrityInjected = (data.flags & Interop.LLKHF_LOWER_IL_INJECTED) != 0;
                    evt.Ours = data.dwExtraInfo == InputSender.Signature;

                    Record(evt);

                    // Arrêt d'urgence : Échap réellement pressé par l'utilisateur.
                    if (!evt.Injected && !keyUp && data.vkCode == Interop.VK_ESCAPE)
                    {
                        AbortRequested = true;
                    }
                }
                catch (Exception)
                {
                    // Une sonde ne doit jamais casser la chaîne d'entrée de l'utilisateur.
                }
            }

            return Interop.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        // -------------------------------------------------------- Raccourci global

        private void RegisterGlobalHotkey()
        {
            if (HotkeyVirtualKey == 0) return;

            try
            {
                // MOD_NOREPEAT évite une avalanche de WM_HOTKEY tant que la touche est tenue :
                // on veut compter des appuis, pas des répétitions clavier.
                if (Interop.RegisterHotKey(IntPtr.Zero, HotkeyId,
                        HotkeyModifiers | Interop.MOD_NOREPEAT, HotkeyVirtualKey))
                {
                    HotkeyRegistered = true;
                }
                else
                {
                    HotkeyError = "RegisterHotKey a échoué (code " + Marshal.GetLastWin32Error() +
                                  ") - raccourci déjà pris par une autre application ?";
                }
            }
            catch (Exception ex)
            {
                HotkeyError = ex.Message;
            }
        }

        // ------------------------------------------------------------- Raw Input

        private void CreateSinkWindow()
        {
            try
            {
                _wndProc = WindowProc;
                lock (NativeCallbackKeepAlive) { NativeCallbackKeepAlive.Add(_wndProc); }

                _moduleHandle = Interop.GetModuleHandle(null);

                Interop.WNDCLASSEX wc = new Interop.WNDCLASSEX();
                wc.cbSize = (uint)Marshal.SizeOf(typeof(Interop.WNDCLASSEX));
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc);
                wc.hInstance = _moduleHandle;
                wc.lpszClassName = _windowClassName;

                if (Interop.RegisterClassEx(ref wc) == 0)
                {
                    RawInputError = "RegisterClassEx a échoué (code " + Marshal.GetLastWin32Error() + ")";
                    return;
                }
                _classRegistered = true;

                _windowHandle = Interop.CreateWindowEx(0, _windowClassName, "OptimusSpike", 0,
                    0, 0, 0, 0, Interop.HWND_MESSAGE, IntPtr.Zero, _moduleHandle, IntPtr.Zero);

                if (_windowHandle == IntPtr.Zero)
                {
                    RawInputError = "CreateWindowEx a échoué (code " + Marshal.GetLastWin32Error() + ")";
                    return;
                }

                Interop.RAWINPUTDEVICE[] devices = new Interop.RAWINPUTDEVICE[1];
                devices[0].usUsagePage = Interop.HID_USAGE_PAGE_GENERIC;
                devices[0].usUsage = Interop.HID_USAGE_GENERIC_KEYBOARD;
                devices[0].dwFlags = Interop.RIDEV_INPUTSINK;
                devices[0].hwndTarget = _windowHandle;

                if (!Interop.RegisterRawInputDevices(devices, 1,
                        (uint)Marshal.SizeOf(typeof(Interop.RAWINPUTDEVICE))))
                {
                    RawInputError = "RegisterRawInputDevices a échoué (code " + Marshal.GetLastWin32Error() + ")";
                    return;
                }

                RawInputRegistered = true;
            }
            catch (Exception ex)
            {
                RawInputError = ex.Message;
            }
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // Une exception qui remonterait dans le code natif est fatale au processus :
            // ce corps entier doit être infaillible.
            try
            {
                if (msg == Interop.WM_INPUT) ReadRawInput(lParam);
            }
            catch (Exception) { }

            return Interop.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void ReadRawInput(IntPtr hRawInput)
        {
            uint headerSize = (uint)Marshal.SizeOf(typeof(Interop.RAWINPUTHEADER));
            uint size = 0;

            if (Interop.GetRawInputData(hRawInput, Interop.RID_INPUT, IntPtr.Zero, ref size, headerSize) != 0)
                return;
            if (size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (Interop.GetRawInputData(hRawInput, Interop.RID_INPUT, buffer, ref size, headerSize) != size)
                    return;

                Interop.RAWINPUTKEYBOARD raw = (Interop.RAWINPUTKEYBOARD)
                    Marshal.PtrToStructure(buffer, typeof(Interop.RAWINPUTKEYBOARD));

                if (raw.header.dwType != Interop.RIM_TYPEKEYBOARD) return;

                ObservedEvent evt = new ObservedEvent();
                evt.Source = "rawinput";
                evt.TimestampUtc = DateTime.UtcNow;
                evt.Ticks = System.Diagnostics.Stopwatch.GetTimestamp();
                evt.ScanCode = raw.keyboard.MakeCode;
                evt.VirtualKey = raw.keyboard.VKey;
                evt.Extended = (raw.keyboard.Flags & Interop.RI_KEY_E0) != 0;
                evt.KeyUp = (raw.keyboard.Flags & Interop.RI_KEY_BREAK) != 0;
                evt.Injected = false; // non exposé par Raw Input
                evt.Ours = raw.keyboard.ExtraInformation == unchecked((uint)InputSender.Signature.ToInt64());

                Record(evt);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // ------------------------------------------------------------- Collecte

        private void Record(ObservedEvent evt)
        {
            lock (_sync)
            {
                _events.Add(evt);
                if (_events.Count > 20000) _events.RemoveRange(0, 10000);
            }
        }

        /// <summary>Vide le journal d'observation (à appeler avant chaque test).</summary>
        public void Clear()
        {
            lock (_sync) { _events.Clear(); }
        }

        /// <summary>Copie du journal courant.</summary>
        public List<ObservedEvent> Snapshot()
        {
            lock (_sync) { return new List<ObservedEvent>(_events); }
        }

        /// <summary>Évènements provenant de nos propres injections, filtrés par source.</summary>
        public List<ObservedEvent> OurEvents(string source)
        {
            List<ObservedEvent> result = new List<ObservedEvent>();
            List<ObservedEvent> all = Snapshot();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Ours && (source == null || all[i].Source == source))
                    result.Add(all[i]);
            }
            return result;
        }

        // ------------------------------------------------------------- Arrêt

        private void Cleanup()
        {
            if (HotkeyRegistered)
            {
                Interop.UnregisterHotKey(IntPtr.Zero, HotkeyId);
                HotkeyRegistered = false;
            }
            if (_hookHandle != IntPtr.Zero)
            {
                Interop.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            if (_windowHandle != IntPtr.Zero)
            {
                Interop.DestroyWindow(_windowHandle);
                _windowHandle = IntPtr.Zero;
            }
            if (_classRegistered)
            {
                // Sans cela, la classe resterait enregistrée dans le processus jusqu'à sa fin,
                // avec un lpfnWndProc devenu inutilisable.
                Interop.UnregisterClass(_windowClassName, _moduleHandle);
                _classRegistered = false;
            }
        }

        public void Dispose()
        {
            if (_thread == null) return;
            _stopping = true;
            try { Interop.PostThreadMessage(_threadId, Interop.WM_QUIT, IntPtr.Zero, IntPtr.Zero); }
            catch (Exception) { }
            _thread.Join(2000);
            _thread = null;
        }
    }
}
