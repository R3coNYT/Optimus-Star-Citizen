namespace Optimus.Core.Domain.Personality;

/// <summary>
/// Les huit curseurs qui définissent le caractère d'un copilote, de 0 à 100.
///
/// Règle qui gouverne ce modèle : <b>chaque trait doit produire un effet observable sans LLM</b>.
/// Un curseur qui ne changerait rien en local serait décoratif, et donc mensonger. Le tableau
/// des effets est dans docs/08.
/// </summary>
public sealed record PersonalityTraits(
    int Humor = 40,
    int Sarcasm = 25,
    int Formality = 80,
    int Verbosity = 30,
    int Aggression = 10,
    int Calmness = 90,
    int Warmth = 45,
    int Confidence = 85)
{
    /// <summary>
    /// Budget de mots d'une réponse, dérivé de la verbosité.
    ///
    /// À 0 le copilote répond par deux mots, à 100 il s'autorise une phrase complète. C'est
    /// l'effet le plus visible du modèle, et celui qui rend un copilote supportable en combat.
    /// </summary>
    public int MaxWords => 4 + (int)Math.Round(Verbosity * 0.20);

    /// <summary>Débit de parole, modulé par le calme. Un copilote posé ne parle pas vite.</summary>
    public double SpeechRate => Math.Round(1.15 - (Calmness / 100.0 * 0.25), 3);

    /// <summary>Valide les bornes ; toute valeur hors de 0-100 est ramenée dans l'intervalle.</summary>
    public PersonalityTraits Clamped() => new(
        Clamp(Humor), Clamp(Sarcasm), Clamp(Formality), Clamp(Verbosity),
        Clamp(Aggression), Clamp(Calmness), Clamp(Warmth), Clamp(Confidence));

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

/// <summary>
/// Vocabulaire propre au copilote.
/// </summary>
/// <param name="AddressUser">Façons de s'adresser au pilote : « commandant », « capitaine »…</param>
/// <param name="ForbiddenPhrases">
/// Expressions bannies. Elles ne servent pas qu'à la coquetterie : c'est ici qu'on interdit
/// « en tant que modèle de langage » et consorts, qui briseraient l'illusion en un mot.
/// </param>
/// <param name="Replacements">Substitutions de vocabulaire : « ok » devient « reçu ».</param>
public sealed record Lexicon(
    IReadOnlyList<string> AddressUser,
    IReadOnlyList<string> ForbiddenPhrases,
    IReadOnlyDictionary<string, string> Replacements)
{
    public static Lexicon Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

/// <summary>Registre de langue du copilote, qui filtre les variantes de réponse.</summary>
[Flags]
public enum SpeechStyle
{
    None = 0,
    Military = 1,
    SciFi = 2,
    Immersive = 4,
    Technical = 8,
}

/// <summary>
/// L'« âme » d'un copilote : ce qui décide non pas de ce qu'il dit, mais de comment il le dit.
/// </summary>
public sealed record Personality(
    PersonalityTraits Traits,
    Lexicon Lexicon,
    SpeechStyle Style = SpeechStyle.Military | SpeechStyle.SciFi | SpeechStyle.Immersive,
    IReadOnlyList<BehaviorRule>? Rules = null)
{
    /// <summary>
    /// Règles de comportement. Ce sont elles qui font qu'un copilote se tait en combat et
    /// explique la cause d'un échec plutôt que de le constater.
    /// </summary>
    public IReadOnlyList<BehaviorRule> Rules { get; init; } = Rules ?? Array.Empty<BehaviorRule>();

    /// <summary>Personnalité neutre, utilisée à défaut de configuration.</summary>
    public static Personality Default { get; } = new(new PersonalityTraits(), Lexicon.Empty);
}
