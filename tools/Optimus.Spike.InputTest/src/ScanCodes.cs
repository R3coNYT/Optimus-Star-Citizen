using System;
using System.Collections.Generic;

namespace Optimus.Spike
{
    /// <summary>Description d'une touche physique.</summary>
    public sealed class KeySpec
    {
        /// <summary>Nom canonique (positions US, comme Star Citizen les nomme : kb1_l → "L").</summary>
        public string Name;

        /// <summary>Make code du jeu de scancodes 1 (celui attendu par SendInput/Raw Input).</summary>
        public ushort ScanCode;

        /// <summary>Touche étendue (préfixe 0xE0) : pavé directionnel, RCtrl, RAlt, pavé num. Entrée…</summary>
        public bool Extended;

        /// <summary>Code de touche virtuelle correspondant sur un clavier US (0 si non pertinent).</summary>
        public ushort VirtualKey;

        public KeySpec(string name, ushort scanCode, bool extended, ushort virtualKey)
        {
            Name = name;
            ScanCode = scanCode;
            Extended = extended;
            VirtualKey = virtualKey;
        }

        public override string ToString()
        {
            return string.Format("{0} (sc=0x{1:X2}{2}, vk=0x{3:X2})",
                Name, ScanCode, Extended ? "+E0" : "", VirtualKey);
        }
    }

    /// <summary>
    /// Table de scancodes **fixes, en positions US**.
    ///
    /// C'est le point technique central du spike : Star Citizen (CryEngine) nomme ses bindings
    /// par position physique US (`kb1_l`, `kb1_a`…). Passer par MapVirtualKey() donnerait un
    /// résultat dépendant de la disposition Windows de l'utilisateur — sur un clavier AZERTY,
    /// MapVirtualKey(VK_A) renvoie le scancode de la position QWERTY 'Q' (0x10), alors que le
    /// jeu attend 0x1E pour `kb1_a`. Le test T7 compare explicitement les deux approches.
    /// </summary>
    public static class ScanCodes
    {
        private static readonly Dictionary<string, KeySpec> ByName =
            new Dictionary<string, KeySpec>(StringComparer.OrdinalIgnoreCase);

        static ScanCodes()
        {
            // Rangée fonction
            Add("ESCAPE", 0x01, false, 0x1B); Alias("ESC", "ESCAPE");
            Add("F1", 0x3B, false, 0x70); Add("F2", 0x3C, false, 0x71);
            Add("F3", 0x3D, false, 0x72); Add("F4", 0x3E, false, 0x73);
            Add("F5", 0x3F, false, 0x74); Add("F6", 0x40, false, 0x75);
            Add("F7", 0x41, false, 0x76); Add("F8", 0x42, false, 0x77);
            Add("F9", 0x43, false, 0x78); Add("F10", 0x44, false, 0x79);
            Add("F11", 0x57, false, 0x7A); Add("F12", 0x58, false, 0x7B);
            // F13-F15 : sans effet sur un clavier standard → touches d'essai idéales
            Add("F13", 0x64, false, 0x7C); Add("F14", 0x65, false, 0x7D); Add("F15", 0x66, false, 0x7E);

            // Rangée chiffres
            Add("1", 0x02, false, 0x31); Add("2", 0x03, false, 0x32); Add("3", 0x04, false, 0x33);
            Add("4", 0x05, false, 0x34); Add("5", 0x06, false, 0x35); Add("6", 0x07, false, 0x36);
            Add("7", 0x08, false, 0x37); Add("8", 0x09, false, 0x38); Add("9", 0x0A, false, 0x39);
            Add("0", 0x0B, false, 0x30);
            Add("MINUS", 0x0C, false, 0xBD); Add("EQUALS", 0x0D, false, 0xBB);
            Add("BACKSPACE", 0x0E, false, 0x08);

            // Rangée du haut
            Add("TAB", 0x0F, false, 0x09);
            Add("Q", 0x10, false, 0x51); Add("W", 0x11, false, 0x57); Add("E", 0x12, false, 0x45);
            Add("R", 0x13, false, 0x52); Add("T", 0x14, false, 0x54); Add("Y", 0x15, false, 0x59);
            Add("U", 0x16, false, 0x55); Add("I", 0x17, false, 0x49); Add("O", 0x18, false, 0x4F);
            Add("P", 0x19, false, 0x50);
            Add("LBRACKET", 0x1A, false, 0xDB); Add("RBRACKET", 0x1B, false, 0xDD);
            Add("ENTER", 0x1C, false, 0x0D);

            // Rangée de repos
            Add("LCTRL", 0x1D, false, 0xA2);
            Add("A", 0x1E, false, 0x41); Add("S", 0x1F, false, 0x53); Add("D", 0x20, false, 0x44);
            Add("F", 0x21, false, 0x46); Add("G", 0x22, false, 0x47); Add("H", 0x23, false, 0x48);
            Add("J", 0x24, false, 0x4A); Add("K", 0x25, false, 0x4B); Add("L", 0x26, false, 0x4C);
            Add("SEMICOLON", 0x27, false, 0xBA); Add("APOSTROPHE", 0x28, false, 0xDE);
            Add("GRAVE", 0x29, false, 0xC0);

            // Rangée basse
            Add("LSHIFT", 0x2A, false, 0xA0); Add("BACKSLASH", 0x2B, false, 0xDC);
            Add("Z", 0x2C, false, 0x5A); Add("X", 0x2D, false, 0x58); Add("C", 0x2E, false, 0x43);
            Add("V", 0x2F, false, 0x56); Add("B", 0x30, false, 0x42); Add("N", 0x31, false, 0x4E);
            Add("M", 0x32, false, 0x4D);
            Add("COMMA", 0x33, false, 0xBC); Add("PERIOD", 0x34, false, 0xBE); Add("SLASH", 0x35, false, 0xBF);
            Add("RSHIFT", 0x36, false, 0xA1);

            // Modificateurs et espace
            Add("LALT", 0x38, false, 0xA4); Add("SPACE", 0x39, false, 0x20);
            Add("CAPSLOCK", 0x3A, false, 0x14);
            Add("RCTRL", 0x1D, true, 0xA3); Add("RALT", 0x38, true, 0xA5);
            Add("LWIN", 0x5B, true, 0x5B); Add("RWIN", 0x5C, true, 0x5C); Add("APPS", 0x5D, true, 0x5D);

            // Pavé numérique
            Add("NUMLOCK", 0x45, false, 0x90); Add("SCROLLLOCK", 0x46, false, 0x91);
            Add("NP_MULTIPLY", 0x37, false, 0x6A); Add("NP_MINUS", 0x4A, false, 0x6D);
            Add("NP_PLUS", 0x4E, false, 0x6B); Add("NP_PERIOD", 0x53, false, 0x6E);
            Add("NP_DIVIDE", 0x35, true, 0x6F); Add("NP_ENTER", 0x1C, true, 0x0D);
            Add("NP_0", 0x52, false, 0x60); Add("NP_1", 0x4F, false, 0x61); Add("NP_2", 0x50, false, 0x62);
            Add("NP_3", 0x51, false, 0x63); Add("NP_4", 0x4B, false, 0x64); Add("NP_5", 0x4C, false, 0x65);
            Add("NP_6", 0x4D, false, 0x66); Add("NP_7", 0x47, false, 0x67); Add("NP_8", 0x48, false, 0x68);
            Add("NP_9", 0x49, false, 0x69);

            // Bloc édition / directions (toutes étendues)
            Add("INSERT", 0x52, true, 0x2D); Add("DELETE", 0x53, true, 0x2E);
            Add("HOME", 0x47, true, 0x24); Add("END", 0x4F, true, 0x23);
            Add("PAGEUP", 0x49, true, 0x21); Add("PAGEDOWN", 0x51, true, 0x22);
            Add("UP", 0x48, true, 0x26); Add("DOWN", 0x50, true, 0x28);
            Add("LEFT", 0x4B, true, 0x25); Add("RIGHT", 0x4D, true, 0x27);

            Alias("CTRL", "LCTRL"); Alias("SHIFT", "LSHIFT"); Alias("ALT", "LALT");
            Alias("RETURN", "ENTER"); Alias("BACK", "BACKSPACE"); Alias("PGUP", "PAGEUP");
            Alias("PGDN", "PAGEDOWN"); Alias("DEL", "DELETE"); Alias("INS", "INSERT");
        }

        private static void Add(string name, ushort scan, bool extended, ushort vk)
        {
            ByName[name] = new KeySpec(name, scan, extended, vk);
        }

        private static void Alias(string alias, string target)
        {
            ByName[alias] = ByName[target];
        }

        /// <summary>Résout un nom de touche. Retourne null si inconnu.</summary>
        public static KeySpec Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            KeySpec spec;
            return ByName.TryGetValue(name.Trim(), out spec) ? spec : null;
        }

        /// <summary>
        /// Scancode obtenu via MapVirtualKey, c'est-à-dire **dépendant de la disposition clavier
        /// Windows active**. Utilisé uniquement pour la comparaison du test T7.
        /// </summary>
        public static ushort ScanCodeFromLayout(ushort virtualKey, out bool extended)
        {
            uint mapped = Interop.MapVirtualKey(virtualKey, Interop.MAPVK_VK_TO_VSC_EX);
            extended = ((mapped >> 8) & 0xFF) == 0xE0;
            return (ushort)(mapped & 0xFF);
        }

        public static List<string> KnownNames()
        {
            List<string> names = new List<string>(ByName.Keys);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
