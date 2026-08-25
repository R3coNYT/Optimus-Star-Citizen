using Optimus.Core.Domain.Bindings;
using Optimus.Core.Loading;
using Optimus.Infrastructure.Input;

namespace Optimus.Core.Tests;

/// <summary>
/// Vérifie la traduction d'une entrée du domaine en évènements Win32, <b>sans rien injecter</b>.
///
/// C'est tout l'intérêt d'avoir séparé la traduction de l'envoi : les scancodes, les préfixes
/// étendus et l'ordre des modificateurs — les trois choses qui font échouer silencieusement une
/// commande en jeu — sont contrôlés en continu par la CI, sur une machine sans clavier.
/// </summary>
public sealed class InputTranslatorTests
{
    [Theory]
    [InlineData("L", 0x26, false)]
    [InlineData("A", 0x1E, false)]
    [InlineData("Q", 0x10, false)]
    [InlineData("W", 0x11, false)]
    [InlineData("Z", 0x2C, false)]
    [InlineData("M", 0x32, false)]
    [InlineData("F5", 0x3F, false)]
    [InlineData("BACKSPACE", 0x0E, false)]
    [InlineData("SPACE", 0x39, false)]
    [InlineData("UP", 0x48, true)]
    [InlineData("DELETE", 0x53, true)]
    [InlineData("RCTRL", 0x1D, true)]
    [InlineData("RALT", 0x38, true)]
    [InlineData("NP_ENTER", 0x1C, true)]
    public void La_table_de_scancodes_suit_les_positions_US(string key, int expectedCode, bool expectedExtended)
    {
        // Ces valeurs sont les positions physiques : elles ne doivent dépendre ni du clavier
        // branché, ni de la disposition Windows. C'est la décision D19, née du spike S0-1 où
        // MapVirtualKey renvoyait la mauvaise touche sur les deux machines du projet.
        Assert.True(ScanCodeMap.TryGet(key, out ScanCode scanCode));
        Assert.Equal((ushort)expectedCode, scanCode.Value);
        Assert.Equal(expectedExtended, scanCode.Extended);
    }

    [Fact]
    public void Un_appui_simple_produit_un_seul_evenement()
    {
        InputSpec input = InputSpec.Simple("L");

        IReadOnlyList<TranslatedInput> press = InputTranslator.BuildPress(input);
        IReadOnlyList<TranslatedInput> release = InputTranslator.BuildRelease(input);

        TranslatedInput down = Assert.Single(press);
        Assert.Equal(TranslatedKind.Keyboard, down.Kind);
        Assert.Equal((ushort)0x26, down.ScanCode);
        Assert.False(down.IsRelease);

        TranslatedInput up = Assert.Single(release);
        Assert.True(up.IsRelease);
        Assert.Equal((ushort)0x26, up.ScanCode);
    }

    [Fact]
    public void Les_modificateurs_encadrent_la_touche()
    {
        // RALT + Y : l'éjection de Star Citizen. Le modificateur doit être enfoncé avant la
        // touche et relâché après, sans quoi le jeu ne voit pas la combinaison.
        InputSpec input = new("Y", ["RALT"]);

        IReadOnlyList<TranslatedInput> press = InputTranslator.BuildPress(input);
        IReadOnlyList<TranslatedInput> release = InputTranslator.BuildRelease(input);

        Assert.Collection(
            press,
            e => AssertKey(e, 0x38, extended: true, release: false),
            e => AssertKey(e, 0x15, extended: false, release: false));

        Assert.Collection(
            release,
            e => AssertKey(e, 0x15, extended: false, release: true),
            e => AssertKey(e, 0x38, extended: true, release: true));
    }

    [Fact]
    public void Plusieurs_modificateurs_sont_relaches_en_ordre_inverse()
    {
        InputSpec input = new("T", ["LCTRL", "LSHIFT"]);

        IReadOnlyList<TranslatedInput> release = InputTranslator.BuildRelease(input);

        Assert.Collection(
            release,
            e => AssertKey(e, 0x14, extended: false, release: true),
            e => AssertKey(e, 0x2A, extended: false, release: true),
            e => AssertKey(e, 0x1D, extended: false, release: true));
    }

    [Fact]
    public void Un_bouton_de_souris_est_traduit_en_bouton()
    {
        InputSpec input = new("MOUSE2", [], InputDevice.Mouse);

        TranslatedInput down = Assert.Single(InputTranslator.BuildPress(input));

        Assert.Equal(TranslatedKind.MouseButton, down.Kind);
        Assert.Equal(MouseButtonKind.Right, down.MouseButton);
        Assert.False(down.IsRelease);
    }

    [Fact]
    public void La_molette_ne_produit_aucun_relachement()
    {
        // Un cran de molette est un évènement instantané : il n'a pas d'état bas, donc rien
        // à relâcher. Le moteur ne doit pas non plus le mémoriser comme touche enfoncée.
        InputSpec input = new("WHEEL_UP", ["LALT"], InputDevice.Mouse);

        TranslatedInput wheel = Assert.Single(InputTranslator.BuildPress(input));
        Assert.Equal(TranslatedKind.MouseWheel, wheel.Kind);
        Assert.Equal(1, wheel.WheelNotches);

        Assert.Empty(InputTranslator.BuildRelease(input));
    }

    [Theory]
    [InlineData("TOUCHE_INEXISTANTE", InputDevice.Keyboard)]
    [InlineData("MOUSE9", InputDevice.Mouse)]
    [InlineData("A", InputDevice.Gamepad)]
    public void Une_entree_non_injectable_est_refusee_avec_une_raison(string key, InputDevice device)
    {
        InputSpec input = new(key, [], device);

        Assert.False(InputTranslator.CanTranslate(input, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void Toutes_les_touches_du_profil_reel_sont_traduisibles()
    {
        // Test dirigé par les données : si Star Citizen utilise une touche que la table ignore,
        // la CI le dit ici plutôt que l'utilisateur devant une commande sans effet.
        BindingProfile profile = JsonCatalogLoader.LoadBindingProfile(
            Path.Combine(TestData.RepositoryRoot, "data", "bindings", "starcitizen", "defaults-4.9.json")).Value;

        List<string> untranslatable = [];

        foreach (Binding binding in profile.Bindings)
        {
            if (binding.Unsupported)
            {
                continue; // axes analogiques et head tracking : hors périmètre, déjà signalés à l'import
            }

            if (!InputTranslator.CanTranslate(binding.Input, out string? reason))
            {
                untranslatable.Add($"{binding.ActionId} ({binding.Input}) : {reason}");
            }
        }

        Assert.True(
            untranslatable.Count == 0,
            $"{untranslatable.Count} binding(s) non traduisible(s) :{Environment.NewLine}" +
            string.Join(Environment.NewLine, untranslatable.Take(20)));
    }

    private static void AssertKey(TranslatedInput input, int scanCode, bool extended, bool release)
    {
        Assert.Equal(TranslatedKind.Keyboard, input.Kind);
        Assert.Equal((ushort)scanCode, input.ScanCode);
        Assert.Equal(extended, input.Extended);
        Assert.Equal(release, input.IsRelease);
    }
}
