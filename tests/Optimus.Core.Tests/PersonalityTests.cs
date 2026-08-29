using Optimus.Core.Domain.Copilots;
using Optimus.Core.Domain.Personality;
using Optimus.Core.Loading;
using Optimus.Core.Personality;

namespace Optimus.Core.Tests;

/// <summary>
/// Comportement du moteur de personnalité, sur le copilote réel du dépôt.
///
/// Ces tests portent sur la propriété la moins évidente à garantir : qu'un copilote paramétré
/// se comporte comme son paramétrage le promet. Un curseur qui ne changerait rien serait un
/// mensonge fait à l'utilisateur.
/// </summary>
public sealed class PersonalityTests
{
    /// <summary>
    /// Le copilote du dépôt, dans une langue <b>nommée</b>.
    ///
    /// Nommée et non laissée au fichier : ces tests portent sur du contenu — « porte »,
    /// « tomber dehors » — et suivraient donc la langue livrée par défaut. Le jour où celle-ci
    /// change, ils tomberaient tous ensemble en donnant l'impression d'une régression du
    /// moteur, alors que seule une valeur de configuration aurait bougé. C'est arrivé.
    /// </summary>
    private static Copilot LoadOptimus(string language = Optimus.Core.Localization.Language.French) =>
        CopilotLoader.Load(
            Path.Combine(TestData.RepositoryRoot, "data", "copilots", "optimus"), language).Value;

    [Fact]
    public void Le_copilote_du_depot_se_charge_sans_anomalie()
    {
        LoadResult<Copilot> result =
            CopilotLoader.Load(Path.Combine(TestData.RepositoryRoot, "data", "copilots", "optimus"));

        Assert.Empty(result.Issues);
        Assert.Equal("optimus", result.Value.Id);
        Assert.Equal("Optimus", result.Value.WakeWord);

        // Aucune voix n'est nommée dans le copilote livré, et c'est délibéré : une voix
        // française nommée en dur n'existe pas sur un Windows anglais, et lirait l'anglais
        // avec un accent sur un Windows français. Vide, Windows choisit la sienne.
        Assert.Null(result.Value.Voice.VoiceId);
        Assert.True(result.Value.Responses.EntryCount >= 25, $"{result.Value.Responses.EntryCount} entrées");
        Assert.True(result.Value.Responses.VariantCount >= 60, $"{result.Value.Responses.VariantCount} variantes");
    }

    [Fact]
    public void Une_commande_avec_replique_dediee_la_prefere_au_generique()
    {
        Copilot optimus = LoadOptimus();
        ResponseComposer composer = new(optimus.Personality, optimus.Responses, seed: 1);

        ComposedResponse? response = composer.ComposeFirst(
            ["ship.doors.toggle", "system.success"], ResponseEvent.Success);

        Assert.NotNull(response);
        Assert.Contains("porte", response!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Une_commande_sans_replique_dediee_retombe_sur_le_generique()
    {
        Copilot optimus = LoadOptimus();
        ResponseComposer composer = new(optimus.Personality, optimus.Responses, seed: 1);

        ComposedResponse? response = composer.ComposeFirst(
            ["commande.sans.replique", "system.success"], ResponseEvent.Success);

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response!.Text));
    }

    [Fact]
    public void Optimus_ne_dit_jamais_deux_fois_de_suite_la_meme_chose()
    {
        // C'est le levier n°1 du realisme : rien ne trahit un automate plus vite que la
        // repetition mot pour mot.
        Copilot optimus = LoadOptimus();
        ResponseComposer composer = new(optimus.Personality, optimus.Responses, seed: 42);

        List<string> said = [];
        for (int i = 0; i < 4; i++)
        {
            ComposedResponse? response = composer.Compose("system.success", ResponseEvent.Success);
            Assert.NotNull(response);
            said.Add(response!.SourceVariant);
        }

        Assert.Equal(said.Count, said.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Le_sarcasme_reste_inaccessible_a_un_copilote_militaire()
    {
        // « Voila. Tachez de ne pas tomber dehors. » exige sarcasm_min 50 ; Optimus est a 25.
        Copilot optimus = LoadOptimus();
        Assert.True(optimus.Personality.Traits.Sarcasm < 50);

        ResponseComposer composer = new(optimus.Personality, optimus.Responses, seed: 7);

        for (int i = 0; i < 20; i++)
        {
            ComposedResponse? response = composer.Compose("ship.doors.toggle", ResponseEvent.Success);
            Assert.NotNull(response);
            Assert.DoesNotContain("tomber dehors", response!.SourceVariant, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Le_meme_catalogue_donne_une_autre_voix_a_un_copilote_sarcastique()
    {
        // La demonstration que personnalite et repliques sont bien decouplees : sans toucher
        // au catalogue, un autre caractere debloque d'autres formulations.
        Copilot optimus = LoadOptimus();

        Domain.Personality.Personality synthia = optimus.Personality with
        {
            Traits = optimus.Personality.Traits with { Sarcasm = 70, Humor = 75, Formality = 40 },
        };

        ResponseComposer composer = new(synthia, optimus.Responses, seed: 3);

        bool sawSarcasm = false;
        for (int i = 0; i < 30; i++)
        {
            ComposedResponse? response = composer.Compose("ship.doors.toggle", ResponseEvent.Success);
            if (response is not null && response.SourceVariant.Contains("tomber dehors", StringComparison.OrdinalIgnoreCase))
            {
                sawSarcasm = true;
                break;
            }
        }

        Assert.True(sawSarcasm, "un copilote sarcastique devrait finir par employer la variante mordante");
    }

    [Fact]
    public void En_combat_les_repliques_restent_courtes()
    {
        Copilot optimus = LoadOptimus();
        ResponseComposer composer = new(optimus.Personality, optimus.Responses, seed: 11);

        for (int i = 0; i < 15; i++)
        {
            ComposedResponse? response = composer.Compose(
                "system.status", ResponseEvent.Any, context: new ResponseContext(CombatActive: true));

            if (response is null)
            {
                continue;
            }

            int words = response.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(words <= 8, $"« {response.Text} » fait {words} mots en plein combat");
        }
    }

    [Fact]
    public void Les_phrases_interdites_ne_peuvent_pas_sortir()
    {
        Copilot optimus = LoadOptimus();
        Assert.Contains("lol", optimus.Personality.Lexicon.ForbiddenPhrases);

        Dictionary<ResponseEvent, List<ResponseVariant>> entry = new()
        {
            [ResponseEvent.Success] =
            [
                new ResponseVariant("lol c'est fait"),
                new ResponseVariant("Exécuté."),
            ],
        };

        ResponseSet set = new("fr-FR", [new KeyValuePair<string, Dictionary<ResponseEvent, List<ResponseVariant>>>("test", entry)]);
        ResponseComposer composer = new(optimus.Personality, set, seed: 5);

        for (int i = 0; i < 10; i++)
        {
            ComposedResponse? response = composer.Compose("test", ResponseEvent.Success);
            Assert.NotNull(response);
            Assert.DoesNotContain("lol", response!.Text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void La_forme_d_adresse_est_tiree_du_lexique()
    {
        Copilot optimus = LoadOptimus();
        ResponseComposer composer = new(optimus.Personality, optimus.Responses, seed: 2);

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 40; i++)
        {
            ComposedResponse? response = composer.Compose("system.status", ResponseEvent.Any);
            if (response is null) continue;

            foreach (string address in optimus.Personality.Lexicon.AddressUser)
            {
                if (response.Text.Contains(address, StringComparison.OrdinalIgnoreCase))
                {
                    seen.Add(address);
                }
            }

            Assert.DoesNotContain("{pilote}", response.Text, StringComparison.Ordinal);
        }

        Assert.NotEmpty(seen);
    }

    [Fact]
    public void La_verbosite_borne_la_longueur_des_repliques()
    {
        Copilot optimus = LoadOptimus();

        // Verbosite 30 -> budget de 10 mots.
        Assert.Equal(10, optimus.Personality.Traits.MaxWords);

        Domain.Personality.Personality laconique = optimus.Personality with
        {
            Traits = optimus.Personality.Traits with { Verbosity = 0 },
        };

        Assert.Equal(4, laconique.Traits.MaxWords);

        ResponseComposer composer = new(laconique, optimus.Responses, seed: 13);
        ComposedResponse? response = composer.Compose("system.status", ResponseEvent.Any);

        Assert.NotNull(response);
        int words = response!.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(words <= 8, $"« {response.Text} » fait {words} mots pour un budget de 4");
    }
}
