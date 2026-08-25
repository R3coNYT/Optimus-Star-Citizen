using Optimus.Core.Domain.Bindings;

namespace Optimus.Core.Bindings;

/// <summary>
/// Traduction entre les noms de touches de Star Citizen et ceux d'Optimus, dans les deux sens.
///
/// L'aller servait déjà à l'import du profil par défaut (<c>tools/convert-default-profile.ps1</c>).
/// Le retour est neuf, et il est indispensable : sans lui, Optimus saurait quelle touche presser
/// sans savoir l'écrire dans un fichier que le jeu accepte de relire — c'est-à-dire sans pouvoir
/// assigner quoi que ce soit pour de bon.
///
/// Les noms suivent les <b>positions</b> du clavier US, comme le jeu les nomme. Sur un AZERTY,
/// la touche marquée « A » est <c>Q</c> ici et pour Star Citizen : les deux se trompent de la
/// même façon, donc ils s'accordent.
/// </summary>
public static class ScKeyNames
{
    private static readonly Dictionary<string, string> ToOptimus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "SPACE", ["tab"] = "TAB", ["enter"] = "ENTER", ["escape"] = "ESCAPE",
        ["backspace"] = "BACKSPACE", ["delete"] = "DELETE", ["insert"] = "INSERT",
        ["home"] = "HOME", ["end"] = "END", ["pgup"] = "PAGEUP", ["pgdn"] = "PAGEDOWN",
        ["up"] = "UP", ["down"] = "DOWN", ["left"] = "LEFT", ["right"] = "RIGHT",
        ["capslock"] = "CAPSLOCK", ["numlock"] = "NUMLOCK", ["scrolllock"] = "SCROLLLOCK",
        ["slash"] = "SLASH", ["backslash"] = "BACKSLASH", ["comma"] = "COMMA",
        ["period"] = "PERIOD", ["semicolon"] = "SEMICOLON", ["apostrophe"] = "APOSTROPHE",
        ["lbracket"] = "LBRACKET", ["rbracket"] = "RBRACKET", ["minus"] = "MINUS",
        ["equals"] = "EQUALS", ["grave"] = "GRAVE",
        ["np_0"] = "NP_0", ["np_1"] = "NP_1", ["np_2"] = "NP_2", ["np_3"] = "NP_3",
        ["np_4"] = "NP_4", ["np_5"] = "NP_5", ["np_6"] = "NP_6", ["np_7"] = "NP_7",
        ["np_8"] = "NP_8", ["np_9"] = "NP_9", ["np_add"] = "NP_PLUS",
        ["np_subtract"] = "NP_MINUS", ["np_multiply"] = "NP_MULTIPLY",
        ["np_divide"] = "NP_DIVIDE", ["np_period"] = "NP_PERIOD", ["np_enter"] = "NP_ENTER",
    };

    private static readonly Dictionary<string, string> MouseToOptimus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mouse1"] = "MOUSE1", ["mouse2"] = "MOUSE2", ["mouse3"] = "MOUSE3", ["mouse4"] = "MOUSE4",
        ["mouse5"] = "MOUSE5", ["mouse6"] = "MOUSE6", ["mouse7"] = "MOUSE7", ["mouse8"] = "MOUSE8",
        ["mwheel_up"] = "WHEEL_UP", ["mwheel_down"] = "WHEEL_DOWN",
    };

    private static readonly Dictionary<string, string> ModifierToOptimus = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lalt"] = "LALT", ["ralt"] = "RALT", ["lshift"] = "LSHIFT", ["rshift"] = "RSHIFT",
        ["lctrl"] = "LCTRL", ["rctrl"] = "RCTRL",
        ["alt"] = "LALT", ["shift"] = "LSHIFT", ["ctrl"] = "LCTRL",
    };

    private static readonly Dictionary<string, string> FromOptimus = Build();

    private static Dictionary<string, string> Build()
    {
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);

        // Le sens inverse se déduit de l'aller, à ceci près que plusieurs noms du jeu peuvent
        // mener au même nom canonique (« alt » et « lalt » donnent tous deux LALT). On garde
        // la forme explicite, seule acceptée sans ambiguïté à la relecture.
        foreach (KeyValuePair<string, string> pair in ToOptimus)
        {
            map.TryAdd(pair.Value, pair.Key);
        }

        foreach (KeyValuePair<string, string> pair in MouseToOptimus)
        {
            map.TryAdd(pair.Value, pair.Key);
        }

        map["LALT"] = "lalt";
        map["RALT"] = "ralt";
        map["LSHIFT"] = "lshift";
        map["RSHIFT"] = "rshift";
        map["LCTRL"] = "lctrl";
        map["RCTRL"] = "rctrl";

        return map;
    }

    /// <summary>Vrai si ce jeton est un modificateur et non une touche principale.</summary>
    public static bool IsModifier(string token) => ModifierToOptimus.ContainsKey(token);

    /// <summary>Nom canonique d'un modificateur du jeu.</summary>
    public static string? Modifier(string token) =>
        ModifierToOptimus.TryGetValue(token, out string? value) ? value : null;

    /// <summary>
    /// Analyse une combinaison telle que le jeu l'écrit : <c>l</c>, <c>lalt+k</c>, <c>mouse1</c>.
    /// Retourne <c>null</c> si rien d'exploitable n'y figure.
    /// </summary>
    public static InputSpec? Parse(string? raw, InputMode mode = InputMode.Tap, int holdMs = InputDefaults.HoldMs)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        List<string> modifiers = new();
        string? main = null;

        foreach (string token in raw.Trim().ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (Modifier(trimmed) is string modifier)
            {
                modifiers.Add(modifier);
                continue;
            }

            // Le dernier jeton non-modificateur l'emporte : les fichiers réels n'en ont jamais deux.
            main = trimmed;
        }

        if (main is null)
        {
            return null;
        }

        modifiers = modifiers.Distinct(StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToList();

        if (MouseToOptimus.TryGetValue(main, out string? mouse))
        {
            return new InputSpec(mouse, modifiers, InputDevice.Mouse, mode, holdMs);
        }

        if (ToOptimus.TryGetValue(main, out string? named))
        {
            return new InputSpec(named, modifiers, InputDevice.Keyboard, mode, holdMs);
        }

        if (main.Length == 1 && (char.IsAsciiLetterOrDigit(main[0])))
        {
            return new InputSpec(main.ToUpperInvariant(), modifiers, InputDevice.Keyboard, mode, holdMs);
        }

        if (main.Length is 2 or 3 && main[0] == 'f' && int.TryParse(main[1..], out int index) && index is >= 1 and <= 24)
        {
            return new InputSpec($"F{index}", modifiers, InputDevice.Keyboard, mode, holdMs);
        }

        return null;
    }

    /// <summary>
    /// Écrit une entrée telle que Star Citizen l'attend dans un fichier de mappage.
    /// Retourne <c>null</c> pour ce qui ne s'écrit pas — molette, axes.
    /// </summary>
    public static string? Format(InputSpec input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!FromOptimus.TryGetValue(input.Key, out string? key))
        {
            // Les lettres, chiffres et touches de fonction s'écrivent en minuscules sans table.
            bool simple = input.Key.Length == 1 && char.IsAsciiLetterOrDigit(input.Key[0]);
            bool function = input.Key.Length is 2 or 3 && input.Key[0] is 'F' or 'f'
                && int.TryParse(input.Key[1..], out int index) && index is >= 1 and <= 24;

            if (!simple && !function)
            {
                return null;
            }

            key = input.Key.ToLowerInvariant();
        }

        if (input.Modifiers.Count == 0)
        {
            return key;
        }

        IEnumerable<string> modifiers = input.Modifiers
            .Select(m => FromOptimus.TryGetValue(m, out string? mapped) ? mapped : m.ToLowerInvariant());

        return string.Join('+', modifiers.Append(key));
    }
}
