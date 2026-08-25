using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Optimus.Spike
{
    /// <summary>
    /// Primitives d'injection clavier/souris via SendInput.
    ///
    /// Toutes les injections portent une signature dans dwExtraInfo (<see cref="Signature"/>),
    /// ce qui permet aux sondes (hook bas niveau, Raw Input) de distinguer nos évènements
    /// synthétiques de ceux produits par le matériel réel de l'utilisateur.
    /// </summary>
    public static class InputSender
    {
        /// <summary>Signature « OPT1 » placée dans dwExtraInfo de chaque évènement injecté.</summary>
        public static readonly IntPtr Signature = new IntPtr(0x4F505431);

        private static readonly int InputSize =
            System.Runtime.InteropServices.Marshal.SizeOf(typeof(Interop.INPUT));

        // ------------------------------------------------------------------ Clavier

        /// <summary>Injecte un évènement clavier en **scancode** (KEYEVENTF_SCANCODE).</summary>
        public static bool SendScanCode(KeySpec key, bool keyUp)
        {
            if (key == null) throw new ArgumentNullException("key");

            uint flags = Interop.KEYEVENTF_SCANCODE;
            if (key.Extended) flags |= Interop.KEYEVENTF_EXTENDEDKEY;
            if (keyUp) flags |= Interop.KEYEVENTF_KEYUP;

            Interop.INPUT[] inputs = new Interop.INPUT[1];
            inputs[0].type = Interop.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0;                 // ignoré quand KEYEVENTF_SCANCODE est présent
            inputs[0].u.ki.wScan = key.ScanCode;
            inputs[0].u.ki.dwFlags = flags;
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = Signature;

            return Interop.SendInput(1, inputs, InputSize) == 1;
        }

        /// <summary>
        /// Injecte un évènement clavier en **code de touche virtuelle uniquement**.
        /// C'est la méthode que beaucoup de moteurs de jeu ignorent : le test T2 le vérifie.
        /// </summary>
        public static bool SendVirtualKey(ushort virtualKey, bool keyUp)
        {
            uint flags = keyUp ? Interop.KEYEVENTF_KEYUP : 0;

            Interop.INPUT[] inputs = new Interop.INPUT[1];
            inputs[0].type = Interop.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = virtualKey;
            inputs[0].u.ki.wScan = 0;
            inputs[0].u.ki.dwFlags = flags;
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = Signature;

            return Interop.SendInput(1, inputs, InputSize) == 1;
        }

        /// <summary>Appui bref : down, maintien <paramref name="holdMs"/>, up.</summary>
        public static void Tap(KeySpec key, double holdMs)
        {
            SendScanCode(key, false);
            PreciseSleep(holdMs);
            SendScanCode(key, true);
        }

        public static void TapVirtualKey(ushort virtualKey, double holdMs)
        {
            SendVirtualKey(virtualKey, false);
            PreciseSleep(holdMs);
            SendVirtualKey(virtualKey, true);
        }

        /// <summary>Double appui.</summary>
        public static void DoubleTap(KeySpec key, double holdMs, double gapMs)
        {
            Tap(key, holdMs);
            PreciseSleep(gapMs);
            Tap(key, holdMs);
        }

        /// <summary>
        /// Combinaison : modificateurs enfoncés, touche tapée, modificateurs relâchés
        /// dans l'ordre inverse. Le relâchement est garanti même en cas d'exception.
        /// </summary>
        public static void Combo(IList<KeySpec> modifiers, KeySpec key, double holdMs)
        {
            List<KeySpec> pressed = new List<KeySpec>();
            try
            {
                for (int i = 0; i < modifiers.Count; i++)
                {
                    SendScanCode(modifiers[i], false);
                    pressed.Add(modifiers[i]);
                    PreciseSleep(15);
                }
                Tap(key, holdMs);
            }
            finally
            {
                for (int i = pressed.Count - 1; i >= 0; i--)
                {
                    SendScanCode(pressed[i], true);
                    PreciseSleep(10);
                }
            }
        }

        // -------------------------------------------------------------------- Souris

        public enum MouseButton { Left, Right, Middle, X1, X2 }

        public static bool SendMouseButton(MouseButton button, bool up)
        {
            uint flags;
            uint data = 0;

            switch (button)
            {
                case MouseButton.Left: flags = up ? Interop.MOUSEEVENTF_LEFTUP : Interop.MOUSEEVENTF_LEFTDOWN; break;
                case MouseButton.Right: flags = up ? Interop.MOUSEEVENTF_RIGHTUP : Interop.MOUSEEVENTF_RIGHTDOWN; break;
                case MouseButton.Middle: flags = up ? Interop.MOUSEEVENTF_MIDDLEUP : Interop.MOUSEEVENTF_MIDDLEDOWN; break;
                case MouseButton.X1: flags = up ? Interop.MOUSEEVENTF_XUP : Interop.MOUSEEVENTF_XDOWN; data = Interop.XBUTTON1; break;
                case MouseButton.X2: flags = up ? Interop.MOUSEEVENTF_XUP : Interop.MOUSEEVENTF_XDOWN; data = Interop.XBUTTON2; break;
                default: throw new ArgumentOutOfRangeException("button");
            }

            Interop.INPUT[] inputs = new Interop.INPUT[1];
            inputs[0].type = Interop.INPUT_MOUSE;
            inputs[0].u.mi.dx = 0;
            inputs[0].u.mi.dy = 0;
            inputs[0].u.mi.mouseData = data;
            inputs[0].u.mi.dwFlags = flags;
            inputs[0].u.mi.time = 0;
            inputs[0].u.mi.dwExtraInfo = Signature;

            return Interop.SendInput(1, inputs, InputSize) == 1;
        }

        public static void TapMouse(MouseButton button, double holdMs)
        {
            SendMouseButton(button, false);
            PreciseSleep(holdMs);
            SendMouseButton(button, true);
        }

        /// <summary>Molette. <paramref name="notches"/> positif = vers le haut.</summary>
        public static bool SendWheel(int notches)
        {
            Interop.INPUT[] inputs = new Interop.INPUT[1];
            inputs[0].type = Interop.INPUT_MOUSE;
            inputs[0].u.mi.mouseData = unchecked((uint)(notches * 120));
            inputs[0].u.mi.dwFlags = Interop.MOUSEEVENTF_WHEEL;
            inputs[0].u.mi.dwExtraInfo = Signature;
            return Interop.SendInput(1, inputs, InputSize) == 1;
        }

        // -------------------------------------------------------------------- Timing

        /// <summary>
        /// Attente précise. Thread.Sleep a une granularité d'environ 15 ms par défaut, ce qui est
        /// inutilisable pour un maintien de 16 ms : on combine Sleep grossier et attente active.
        /// </summary>
        public static void PreciseSleep(double milliseconds)
        {
            if (milliseconds <= 0) return;

            Stopwatch sw = Stopwatch.StartNew();
            double remaining = milliseconds;

            // Au-delà de 20 ms, on dort (en gardant une marge de 12 ms pour l'attente active).
            if (remaining > 20)
            {
                Thread.Sleep((int)(remaining - 12));
            }

            while (sw.Elapsed.TotalMilliseconds < milliseconds)
            {
                Thread.SpinWait(40);
            }
        }

        /// <summary>Passe la résolution du timer système à 1 ms pour la durée du spike.</summary>
        public sealed class HighResolutionTimerScope : IDisposable
        {
            private bool _active;

            public HighResolutionTimerScope()
            {
                try { _active = Interop.TimeBeginPeriod(1) == 0; }
                catch (Exception) { _active = false; }
            }

            public void Dispose()
            {
                if (!_active) return;
                try { Interop.TimeEndPeriod(1); }
                catch (Exception) { }
                _active = false;
            }
        }
    }
}
