namespace Optimus.Infrastructure.Input;

/// <summary>Touche physique : son make code du jeu de scancodes 1, et son éventuel préfixe étendu.</summary>
/// <param name="Name">Nom canonique, en position clavier US.</param>
/// <param name="ScanCode">Make code.</param>
/// <param name="Extended">Touche étendue, préfixée 0xE0 (pavé directionnel, RCtrl, RAlt, Entrée du pavé numérique).</param>
public readonly record struct ScanCode(string Name, ushort Value, bool Extended);

/// <summary>
/// Table <b>fixe</b> des scancodes, en positions clavier US.
///
/// C'est la décision D19, et elle repose sur une mesure, pas sur une préférence : le spike S0-1
/// a montré que <c>MapVirtualKey(VK_A)</c> renvoie 0x10 — la position QWERTY « Q » — sur les deux
/// machines du projet, toutes deux en AZERTY, alors que Star Citizen entend 0x1E pour <c>kb1_a</c>.
/// Une conversion dépendante de la disposition Windows enverrait donc la mauvaise touche chez la
/// majorité des joueurs francophones.
///
/// Ces valeurs ne dépendent ni de la langue, ni du clavier, ni du système : ce sont les positions
/// physiques, exactement ce que lit le moteur du jeu via le Raw Input.
/// </summary>
public static class ScanCodeMap
{
    private static readonly Dictionary<string, ScanCode> ByName = BuildTable();

    /// <summary>Nombre de touches connues.</summary>
    public static int Count => ByName.Count;

    /// <summary>Résout un nom de touche. Insensible à la casse.</summary>
    public static bool TryGet(string? name, out ScanCode scanCode)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            return ByName.TryGetValue(name.Trim(), out scanCode);
        }

        scanCode = default;
        return false;
    }

    /// <summary>Résout un nom de touche ou lève. Réservé aux cas déjà validés en amont.</summary>
    public static ScanCode Get(string name) =>
        TryGet(name, out ScanCode scanCode)
            ? scanCode
            : throw new KeyNotFoundException($"Touche inconnue : « {name} ».");

    /// <summary>Noms reconnus, triés — utile à l'interface de capture et aux messages d'erreur.</summary>
    public static IReadOnlyList<string> KnownNames() =>
        ByName.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    private static Dictionary<string, ScanCode> BuildTable()
    {
        Dictionary<string, ScanCode> table = new(StringComparer.OrdinalIgnoreCase);

        void Add(string name, ushort code, bool extended = false) =>
            table[name] = new ScanCode(name, code, extended);

        void Alias(string alias, string target) => table[alias] = table[target];

        // Rangée fonction
        Add("ESCAPE", 0x01);
        Add("F1", 0x3B); Add("F2", 0x3C); Add("F3", 0x3D); Add("F4", 0x3E);
        Add("F5", 0x3F); Add("F6", 0x40); Add("F7", 0x41); Add("F8", 0x42);
        Add("F9", 0x43); Add("F10", 0x44); Add("F11", 0x57); Add("F12", 0x58);

        // F13 à F15 : sans effet sur un clavier standard, donc idéales pour les essais.
        Add("F13", 0x64); Add("F14", 0x65); Add("F15", 0x66);

        // Rangée chiffres
        Add("1", 0x02); Add("2", 0x03); Add("3", 0x04); Add("4", 0x05); Add("5", 0x06);
        Add("6", 0x07); Add("7", 0x08); Add("8", 0x09); Add("9", 0x0A); Add("0", 0x0B);
        Add("MINUS", 0x0C); Add("EQUALS", 0x0D); Add("BACKSPACE", 0x0E);

        // Rangée du haut
        Add("TAB", 0x0F);
        Add("Q", 0x10); Add("W", 0x11); Add("E", 0x12); Add("R", 0x13); Add("T", 0x14);
        Add("Y", 0x15); Add("U", 0x16); Add("I", 0x17); Add("O", 0x18); Add("P", 0x19);
        Add("LBRACKET", 0x1A); Add("RBRACKET", 0x1B); Add("ENTER", 0x1C);

        // Rangée de repos
        Add("LCTRL", 0x1D);
        Add("A", 0x1E); Add("S", 0x1F); Add("D", 0x20); Add("F", 0x21); Add("G", 0x22);
        Add("H", 0x23); Add("J", 0x24); Add("K", 0x25); Add("L", 0x26);
        Add("SEMICOLON", 0x27); Add("APOSTROPHE", 0x28); Add("GRAVE", 0x29);

        // Rangée basse
        Add("LSHIFT", 0x2A); Add("BACKSLASH", 0x2B);
        Add("Z", 0x2C); Add("X", 0x2D); Add("C", 0x2E); Add("V", 0x2F); Add("B", 0x30);
        Add("N", 0x31); Add("M", 0x32);
        Add("COMMA", 0x33); Add("PERIOD", 0x34); Add("SLASH", 0x35); Add("RSHIFT", 0x36);

        // Modificateurs et espace
        Add("LALT", 0x38); Add("SPACE", 0x39); Add("CAPSLOCK", 0x3A);
        Add("RCTRL", 0x1D, extended: true);
        Add("RALT", 0x38, extended: true);
        Add("LWIN", 0x5B, extended: true);
        Add("RWIN", 0x5C, extended: true);
        Add("APPS", 0x5D, extended: true);

        // Pavé numérique
        Add("NUMLOCK", 0x45); Add("SCROLLLOCK", 0x46);
        Add("NP_MULTIPLY", 0x37); Add("NP_MINUS", 0x4A); Add("NP_PLUS", 0x4E); Add("NP_PERIOD", 0x53);
        Add("NP_DIVIDE", 0x35, extended: true);
        Add("NP_ENTER", 0x1C, extended: true);
        Add("NP_0", 0x52); Add("NP_1", 0x4F); Add("NP_2", 0x50); Add("NP_3", 0x51); Add("NP_4", 0x4B);
        Add("NP_5", 0x4C); Add("NP_6", 0x4D); Add("NP_7", 0x47); Add("NP_8", 0x48); Add("NP_9", 0x49);

        // Bloc édition et directions : toutes étendues
        Add("INSERT", 0x52, extended: true); Add("DELETE", 0x53, extended: true);
        Add("HOME", 0x47, extended: true); Add("END", 0x4F, extended: true);
        Add("PAGEUP", 0x49, extended: true); Add("PAGEDOWN", 0x51, extended: true);
        Add("UP", 0x48, extended: true); Add("DOWN", 0x50, extended: true);
        Add("LEFT", 0x4B, extended: true); Add("RIGHT", 0x4D, extended: true);

        Alias("ESC", "ESCAPE");
        Alias("CTRL", "LCTRL"); Alias("SHIFT", "LSHIFT"); Alias("ALT", "LALT");
        Alias("RETURN", "ENTER"); Alias("BACK", "BACKSPACE");
        Alias("PGUP", "PAGEUP"); Alias("PGDN", "PAGEDOWN");
        Alias("DEL", "DELETE"); Alias("INS", "INSERT");

        return table;
    }
}
