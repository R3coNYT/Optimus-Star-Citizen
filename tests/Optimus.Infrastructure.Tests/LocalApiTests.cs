using Optimus.Core.Api;
using Optimus.Infrastructure.Api;

namespace Optimus.Infrastructure.Tests;

/// <summary>
/// L'API locale.
///
/// Ce qui est éprouvé ici n'est pas le transport — HTTP fonctionne — mais les trois promesses
/// que l'API doit tenir : elle n'écoute que la boucle locale, elle n'admet que le porteur du
/// jeton, et elle ne laisse pas un client emballé marteler le clavier du pilote.
/// </summary>
public sealed class LocalApiTests : IDisposable
{
    private readonly string _root;

    public LocalApiTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"optimus-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Un dossier temporaire qui survit n'est pas un echec d'essai.
        }
    }

    private string Tokens => Path.Combine(_root, "tokens.dat");

    // ------------------------------------------------------------- le jeton d un navigateur

    /// <summary>
    /// L’en-tête reste la voie normale.
    ///
    /// Ouvrir la WebSocket aux sous-protocoles ne doit rien retirer aux clients qui savent poser
    /// un en-tête — c’est-à-dire tous sauf les navigateurs.
    /// </summary>
    [Fact]
    public void L_en_tete_Authorization_passe_en_premier()
    {
        IReadOnlyList<string> presented = LocalApiServer.Presented("Bearer abc", null);

        Assert.Equal(["abc"], presented);
    }

    /// <summary>
    /// Un navigateur ne peut PAS poser d’en-tête sur une WebSocket.
    ///
    /// <c>new WebSocket(url, ["optimus.v1", token])</c> est la seule voie que l’API JavaScript
    /// laisse pour porter un secret. Le serveur doit donc lire les sous-protocoles — et écarter
    /// le nom du protocole, qui annonce la version et n’authentifie rien.
    /// </summary>
    [Fact]
    public void Le_sous_protocole_porte_le_secret_et_pas_le_nom_du_protocole()
    {
        IReadOnlyList<string> presented =
            LocalApiServer.Presented(null, "optimus.v1, le-secret");

        Assert.Equal(["le-secret"], presented);
    }

    /// <summary>
    /// Un jeton est émis en base64url sans remplissage. La RFC 6455 admet ces caractères dans un
    /// nom de sous-protocole, donc il voyage tel quel : aucun réencodage, aucune occasion de se
    /// tromper. Cet essai fige la promesse.
    /// </summary>
    [Fact]
    public void Un_vrai_jeton_traverse_le_sous_protocole_sans_retouche()
    {
        ApiToken token = ApiToken.Issue("Stream Deck", ApiScope.All);

        Assert.DoesNotContain(token.Secret, c => c is '+' or '/' or '=' or ',' or ' ');

        IReadOnlyList<string> presented =
            LocalApiServer.Presented(null, $"{LocalApiServer.Subprotocol},{token.Secret}");

        Assert.Equal([token.Secret], presented);
        Assert.True(token.Matches(presented[0]));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Basic abc", null)]
    [InlineData(null, "optimus.v1")]
    public void Rien_de_presentable_ne_donne_aucun_candidat(string? authorization, string? subprotocols)
    {
        Assert.Empty(LocalApiServer.Presented(authorization, subprotocols));
    }

    /// <summary>
    /// Les deux voies peuvent coexister, et l’ordre compte : l’en-tête est le plus explicite des
    /// deux, il est donc essayé d’abord.
    /// </summary>
    [Fact]
    public void Les_deux_voies_coexistent_l_en_tete_d_abord()
    {
        IReadOnlyList<string> presented =
            LocalApiServer.Presented("Bearer entete", "optimus.v1, sous-protocole");

        Assert.Equal(["entete", "sous-protocole"], presented);
    }

    // --------------------------------------------------------------- ouvrir et fermer le micro

    /// <summary>
    /// La bascule du micro distingue « absent » de « faux ».
    ///
    /// C'est toute la route <c>/api/system/listening</c> : un corps qui ne dit rien bascule,
    /// un corps qui dit impose. Sans cette distinction, une touche de Stream Deck qui envoie
    /// un corps vide vaudrait « éteins » — et le micro ne se rouvrirait jamais depuis le
    /// boîtier.
    ///
    /// Le reste de la route n'a rien à éprouver ici : elle appelle StartListeningAsync ou
    /// StopListeningAsync, qui demandent un micro.
    /// </summary>
    [Theory]
    [InlineData("{\"listening\":true}", false, true)]
    [InlineData("{\"listening\":true}", true, true)]
    [InlineData("{\"listening\":false}", true, false)]
    [InlineData("{\"listening\":false}", false, false)]
    [InlineData("{}", false, false)]
    [InlineData("{}", true, true)]
    [InlineData("{\"autre\":1}", true, true)]
    public void Le_drapeau_distingue_absent_de_faux(string body, bool fallback, bool expected)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(body);

        Assert.Equal(
            expected,
            LocalApiServer.Flag(document.RootElement, "listening", fallback));
    }

    /// <summary>
    /// Un corps qui n'est pas un objet ne décide de rien.
    ///
    /// Le cas arrive pour de vrai : <c>curl -d ""</c> envoie un corps vide, que le serveur
    /// présente comme <c>Undefined</c>. Le prendre pour un « faux » couperait le micro à
    /// chaque appui.
    /// </summary>
    [Theory]
    [InlineData("[]")]
    [InlineData("\"texte\"")]
    [InlineData("null")]
    public void Un_corps_qui_n_est_pas_un_objet_laisse_le_repli_decider(string body)
    {
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(body);

        Assert.True(LocalApiServer.Flag(document.RootElement, "listening", fallback: true));
        Assert.False(LocalApiServer.Flag(document.RootElement, "listening", fallback: false));
    }

    // ------------------------------------------------------------------- la boucle locale

    /// <summary>
    /// L'adresse d'écoute est <b>toujours</b> 127.0.0.1.
    ///
    /// Cet essai n'existe pas pour vérifier une chaîne de caractères mais pour qu'une main
    /// future qui voudrait « rendre l'hôte configurable » se heurte à un refus explicite.
    /// Windows refuse déjà l'écoute sur toutes les interfaces à qui n'est pas administrateur —
    /// mais on ne s'en remet pas au système pour une promesse qu'on a faite (§81-83).
    /// </summary>
    [Theory]
    [InlineData(8731)]
    [InlineData(1024)]
    [InlineData(65535)]
    public void L_adresse_d_ecoute_est_toujours_la_boucle_locale(int port)
    {
        Assert.Equal($"http://127.0.0.1:{port}/", new ApiSettings(Port: port).Prefix);
    }

    [Fact]
    public void L_api_est_eteinte_par_defaut()
    {
        Assert.False(ApiSettings.Disabled.Enabled);
        Assert.False(new ApiSettings().Enabled);
    }

    // ------------------------------------------------------------------------- les jetons

    [Fact]
    public void Deux_jetons_emis_ne_se_ressemblent_pas()
    {
        ApiToken first = ApiToken.Issue("a", ApiScope.Read);
        ApiToken second = ApiToken.Issue("b", ApiScope.Read);

        Assert.NotEqual(first.Secret, second.Secret);
        Assert.True(first.Secret.Length >= 40, $"secret trop court : {first.Secret.Length}");
    }

    [Fact]
    public void Un_jeton_ne_reconnait_que_son_propre_secret()
    {
        ApiToken token = ApiToken.Issue("Optimus", ApiScope.All);

        Assert.True(token.Matches(token.Secret));
        Assert.False(token.Matches(token.Secret + "x"));
        Assert.False(token.Matches(token.Secret[..^1]));
        Assert.False(token.Matches("n'importe quoi"));
    }

    /// <summary>Ni <c>null</c> ni le vide ne doivent ouvrir la porte.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_secret_absent_n_ouvre_rien(string? candidate)
    {
        Assert.False(ApiToken.Issue("Optimus", ApiScope.All).Matches(candidate));
    }

    /// <summary>
    /// Les portées se cumulent, et une portée n'en implique pas une autre.
    ///
    /// Un client de lecture ne doit pas exécuter par effet de bord d'un ou binaire mal posé.
    /// </summary>
    [Fact]
    public void Une_portee_de_lecture_n_autorise_pas_l_execution()
    {
        ApiScope read = ApiScope.Read;

        Assert.True(read.HasFlag(ApiScope.Read));
        Assert.False(read.HasFlag(ApiScope.Write));
        Assert.False(read.HasFlag(ApiScope.Execute));

        Assert.True(ApiScope.All.HasFlag(ApiScope.Execute));
    }

    // -------------------------------------------------------------------------- le magasin

    [Fact]
    public void Un_magasin_absent_ne_contient_aucun_jeton()
    {
        Assert.Empty(ApiTokenStore.Load(Tokens));
    }

    [Fact]
    public void Un_jeton_ecrit_se_relit_a_l_identique()
    {
        ApiToken written = ApiToken.Issue("Discord", ApiScope.Read | ApiScope.Write);

        ApiTokenStore.Save([written], Tokens);
        ApiToken read = Assert.Single(ApiTokenStore.Load(Tokens));

        Assert.Equal(written.Name, read.Name);
        Assert.Equal(written.Secret, read.Secret);
        Assert.Equal(written.Scopes, read.Scopes);
    }

    /// <summary>
    /// Le fichier écrit ne doit pas contenir le secret en clair.
    ///
    /// C'est tout l'objet du chiffrement : le pilote recopie <c>%APPDATA%\Optimus</c> d'une
    /// machine à l'autre, et un jeton lisible partirait avec, sur une clé USB ou dans une
    /// sauvegarde.
    /// </summary>
    [Fact]
    public void Le_secret_n_apparait_pas_en_clair_sur_le_disque()
    {
        ApiToken token = ApiToken.Issue("Optimus", ApiScope.All);

        ApiTokenStore.Save([token], Tokens);

        string raw = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Tokens));

        Assert.DoesNotContain(token.Secret, raw, StringComparison.Ordinal);
    }

    /// <summary>Un fichier illisible ne doit pas empêcher Optimus de démarrer.</summary>
    [Fact]
    public void Un_magasin_corrompu_est_traite_comme_vide()
    {
        File.WriteAllText(Tokens, "ceci n'est pas un fichier chiffré");

        Assert.Empty(ApiTokenStore.Load(Tokens));
    }

    [Fact]
    public void Le_jeton_du_pilote_est_emis_d_office()
    {
        ApiToken token = Assert.Single(ApiTokenStore.Ensure(Tokens));

        Assert.Equal(ApiTokenStore.OwnerName, token.Name);
        Assert.Equal(ApiScope.All, token.Scopes);
        Assert.True(File.Exists(Tokens));
    }

    [Fact]
    public void Une_seconde_lecture_rend_le_meme_jeton()
    {
        ApiToken first = ApiTokenStore.Ensure(Tokens).Single();
        ApiToken again = ApiTokenStore.Ensure(Tokens).Single();

        Assert.Equal(first.Secret, again.Secret);
    }

    /// <summary>Régénérer doit invalider l'ancien secret : c'est la seule raison de le faire.</summary>
    [Fact]
    public void Regenerer_remplace_le_secret_et_garde_la_portee()
    {
        ApiTokenStore.Save([ApiToken.Issue("Discord", ApiScope.Read)], Tokens);

        ApiToken before = ApiTokenStore.Load(Tokens).Single();
        ApiToken after = ApiTokenStore.Regenerate("Discord", Tokens);

        Assert.NotEqual(before.Secret, after.Secret);
        Assert.Equal(ApiScope.Read, after.Scopes);
        Assert.Single(ApiTokenStore.Load(Tokens));
        Assert.Equal(after.Secret, ApiTokenStore.Load(Tokens).Single().Secret);
    }

    // --------------------------------------------------------------------------- le plafond

    [Fact]
    public void Le_plafond_laisse_passer_puis_refuse()
    {
        ApiRateLimiter limiter = new(3, () => 0);

        Assert.True(limiter.Allow("client"));
        Assert.True(limiter.Allow("client"));
        Assert.True(limiter.Allow("client"));
        Assert.False(limiter.Allow("client"));
    }

    /// <summary>Un client emballé ne doit pas condamner les autres.</summary>
    [Fact]
    public void Le_plafond_est_compte_par_client()
    {
        ApiRateLimiter limiter = new(1, () => 0);

        Assert.True(limiter.Allow("discord"));
        Assert.False(limiter.Allow("discord"));
        Assert.True(limiter.Allow("streamdeck"));
    }

    /// <summary>
    /// La fenêtre glisse : passé une minute, les anciens appels ne comptent plus.
    ///
    /// Éprouvé sur une horloge injectée plutôt qu'en dormant soixante secondes — un essai qui
    /// dort une minute finit par ne plus être joué du tout.
    /// </summary>
    [Fact]
    public void La_fenetre_glisse_au_bout_d_une_minute()
    {
        long now = 0;
        ApiRateLimiter limiter = new(2, () => now);

        Assert.True(limiter.Allow("client"));
        Assert.True(limiter.Allow("client"));
        Assert.False(limiter.Allow("client"));

        now = 59_999;
        Assert.False(limiter.Allow("client"));

        now = 60_000;
        Assert.True(limiter.Allow("client"));
    }
}
