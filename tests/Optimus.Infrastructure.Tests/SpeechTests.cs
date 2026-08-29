using Optimus.Core.Abstractions;
using Optimus.Infrastructure.Speech;

namespace Optimus.Infrastructure.Tests;

/// <summary>
/// La couche de parole, éprouvée sans carte son ni Piper installé.
///
/// Ce qui compte ici n'est pas le timbre — il s'écoute, il ne s'affirme pas — mais la promesse
/// qui l'entoure : <b>rien ne doit pouvoir rendre le copilote muet</b>. Un moteur qui échoue,
/// une installation à moitié faite, une voix qui n'existe pas : chacun de ces cas a sa réponse,
/// et aucune n'est le silence.
/// </summary>
public sealed class SpeechTests
{
    // ----------------------------------------------------------- la voix suit la langue
    //
    // Defaut mesure le 2026-08-29, sur une machine dont Windows s'affiche en francais :
    // le copilote passe en anglais prononcait un texte anglais avec une voix francaise.
    // « voice_id » valant null, la selection rendait la main sans rien poser, et le
    // synthetiseur gardait la voix par defaut du systeme. Le contrat promettait pourtant
    // « voix par defaut du moteur POUR LA LANGUE » depuis le premier jour.
    //
    // Ces essais ne postulent aucune voix en particulier : ils interrogent celles qui sont
    // reellement installees. Exiger l'anglais ferait echouer l'essai sur une machine
    // francaise sans que rien ne soit casse — et c'est exactement ce qui vient d'arriver.

    /// <summary>Etiquettes de langue effectivement installees, par exemple <c>fr-FR</c>.</summary>
    private static IReadOnlyList<string> InstalledLanguages() =>
        Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
            .Select(v => v.Language)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [Fact]
    public void ChaqueLangueInstalleeTrouveUneVoixDeCetteLangue()
    {
        IReadOnlyList<string> languages = InstalledLanguages();

        Assert.NotEmpty(languages);

        foreach (string language in languages)
        {
            Windows.Media.SpeechSynthesis.VoiceInformation? voice =
                WindowsTtsProvider.MatchLanguage(language);

            Assert.NotNull(voice);
            Assert.Equal(language, voice!.Language, ignoreCase: true);
        }
    }

    [Fact]
    public void LeCodeDeLangueSeulSuffit()
    {
        // « en » doit trouver en-GB quand en-US manque : une voix britannique dit l'anglais
        // infiniment mieux qu'une voix francaise.
        foreach (string language in InstalledLanguages())
        {
            string prefix = language.Split('-')[0];

            Windows.Media.SpeechSynthesis.VoiceInformation? voice =
                WindowsTtsProvider.MatchLanguage(prefix);

            Assert.NotNull(voice);
            Assert.StartsWith(prefix, voice!.Language, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void UneLangueAbsenteNeRendRien()
    {
        // Rendre null est ce qui permet a l'appelant de retomber sur la voix par defaut
        // plutot que sur une voix prise au hasard — et de le dire au journal.
        Assert.Null(WindowsTtsProvider.MatchLanguage("zz-ZZ"));
    }

    [Fact]
    public void SansLangueOnNeChoisitPas()
    {
        Assert.Null(WindowsTtsProvider.MatchLanguage(null));
        Assert.Null(WindowsTtsProvider.MatchLanguage("   "));
    }

    /// <summary>Un moteur dont on décide s'il tombe, et qui compte ce qu'on lui demande.</summary>
    private sealed class StubTts(string id, bool fails = false) : ITextToSpeechProvider
    {
        public string Id => id;

        public int Spoken { get; private set; }

        public bool Warmed { get; private set; }

        public IReadOnlyList<VoiceInfo> Available { get; init; } = Array.Empty<VoiceInfo>();

        public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Available);

        public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fails)
            {
                throw new InvalidOperationException($"« {id} » refuse de parler.");
            }

            Spoken++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default)
        {
            Warmed = true;

            return fails
                ? Task.FromException(new InvalidOperationException("préchauffage impossible"))
                : Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static SpeechRequest Line => new("Vaisseau paré.");

    // ------------------------------------------------------------------------------ le repli

    [Fact]
    public async Task Le_moteur_principal_parle_tant_qu_il_tient()
    {
        StubTts piper = new("piper");
        StubTts windows = new("windows-onecore");

        await using FallbackTtsProvider speech = new(piper, windows);

        await speech.SpeakAsync(Line);

        Assert.Equal(1, piper.Spoken);
        Assert.Equal(0, windows.Spoken);
    }

    /// <summary>Un échec isolé change la voix, jamais le fait de parler.</summary>
    [Fact]
    public async Task Un_echec_fait_parler_le_repli()
    {
        StubTts piper = new("piper", fails: true);
        StubTts windows = new("windows-onecore");

        await using FallbackTtsProvider speech = new(piper, windows);

        await speech.SpeakAsync(Line);

        Assert.Equal(1, windows.Spoken);
        Assert.False(speech.IsDemoted);
    }

    /// <summary>
    /// Le point qui justifie la rétrogradation.
    ///
    /// Réessayer indéfiniment ferait payer le délai d'attente de Piper — vingt secondes — à
    /// <i>chaque</i> réplique, ce qui serait bien pire qu'un simple changement de timbre. Après
    /// deux échecs, le moteur principal n'est plus sollicité du tout.
    /// </summary>
    [Fact]
    public async Task Deux_echecs_abandonnent_le_moteur_principal_pour_la_session()
    {
        CountingFailure piper = new();
        StubTts windows = new("windows-onecore");

        await using FallbackTtsProvider speech = new(piper, windows);

        await speech.SpeakAsync(Line);
        await speech.SpeakAsync(Line);
        await speech.SpeakAsync(Line);
        await speech.SpeakAsync(Line);

        Assert.True(speech.IsDemoted);
        Assert.Equal(2, piper.Attempts);
        Assert.Equal(4, windows.Spoken);
        Assert.Equal("windows-onecore", speech.Id);
    }

    /// <summary>Un moteur qui compte ses tentatives, pour prouver qu'on cesse de l'appeler.</summary>
    private sealed class CountingFailure : ITextToSpeechProvider
    {
        public int Attempts { get; private set; }

        public string Id => "piper";

        public Task<IReadOnlyList<VoiceInfo>> GetVoicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VoiceInfo>>(Array.Empty<VoiceInfo>());

        public Task SpeakAsync(SpeechRequest request, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("toujours en panne");
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WarmUpAsync(string? voiceId = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Une interruption demandée n'est pas une panne.
    ///
    /// Le pilote qui reprend le micro coupe la parole du copilote ; compter cela comme un échec
    /// finirait par abandonner Piper après deux interruptions, pour une raison qui n'a rien à
    /// voir avec lui.
    /// </summary>
    [Fact]
    public async Task Une_interruption_n_est_pas_comptee_comme_un_echec()
    {
        StubTts piper = new("piper");
        StubTts windows = new("windows-onecore");

        await using FallbackTtsProvider speech = new(piper, windows);

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => speech.SpeakAsync(Line, cancelled.Token));

        Assert.False(speech.IsDemoted);
        Assert.Equal(0, windows.Spoken);
    }

    /// <summary>
    /// Les voix des deux moteurs, pour que choisir une voix Windows ne demande pas d'abord de
    /// désactiver Piper.
    /// </summary>
    [Fact]
    public async Task Les_voix_des_deux_moteurs_sont_proposees()
    {
        StubTts piper = new("piper") { Available = [new VoiceInfo("fr_FR-tom-medium", "fr_FR-tom-medium", "fr-FR")] };
        StubTts windows = new("windows-onecore") { Available = [new VoiceInfo("paul", "Microsoft Paul", "fr-FR")] };

        await using FallbackTtsProvider speech = new(piper, windows);

        IReadOnlyList<VoiceInfo> voices = await speech.GetVoicesAsync();

        Assert.Equal(2, voices.Count);
        Assert.Equal("fr_FR-tom-medium", voices[0].Id);
    }

    /// <summary>
    /// Le repli est préchauffé lui aussi, et un préchauffage raté n'empêche rien.
    ///
    /// Sans cela, la première réplique après une panne paierait les 429 ms d'initialisation des
    /// voix Windows (D23) en plus de l'échec qui l'a provoquée.
    /// </summary>
    [Fact]
    public async Task Le_repli_est_prechauffe_meme_si_le_principal_echoue()
    {
        StubTts piper = new("piper", fails: true);
        StubTts windows = new("windows-onecore");

        await using FallbackTtsProvider speech = new(piper, windows);

        await speech.WarmUpAsync("fr_FR-tom-medium");

        Assert.True(windows.Warmed);
    }

    // ------------------------------------------------------------------------ l'installation

    [Fact]
    public void Sans_binaire_il_n_y_a_pas_d_installation()
    {
        using TempTree tree = new();

        Assert.Null(PiperInstallation.Locate(tree.Path));
    }

    /// <summary>
    /// Un Piper sans modèle est une installation à moitié faite.
    ///
    /// La retenir rendrait Optimus muet le temps que le pilote comprenne pourquoi — alors que
    /// la refuser lui donne les voix Windows et une ligne de journal qui dit quoi faire.
    /// </summary>
    [Fact]
    public void Un_binaire_sans_voix_ne_compte_pas_comme_une_installation()
    {
        using TempTree tree = new();
        tree.Write("piper.exe", "binaire d'essai");

        Assert.Null(PiperInstallation.Locate(tree.Path));
    }

    [Fact]
    public void Le_binaire_et_une_voix_suffisent()
    {
        using TempTree tree = new();
        tree.Write("piper.exe", "binaire d'essai");
        tree.Write(Path.Combine("voices", "fr_FR-tom-medium.onnx"), "modèle d'essai");
        tree.Write(
            Path.Combine("voices", "fr_FR-tom-medium.onnx.json"),
            """{"language":{"code":"fr_FR"}}""");

        PiperInstallation? found = PiperInstallation.Locate(tree.Path);

        Assert.NotNull(found);

        VoiceInfo voice = Assert.Single(found.Voices());
        Assert.Equal("fr_FR-tom-medium", voice.Id);
        Assert.Equal("fr-FR", voice.Language);
        Assert.Null(voice.IsMale);
    }

    /// <summary>Une configuration illisible ne doit pas faire disparaître la voix elle-même.</summary>
    [Fact]
    public void Une_configuration_cassee_laisse_la_voix_utilisable()
    {
        using TempTree tree = new();
        tree.Write("piper.exe", "binaire d'essai");
        tree.Write(Path.Combine("voices", "abimee.onnx"), "modèle d'essai");
        tree.Write(Path.Combine("voices", "abimee.onnx.json"), "{ ceci n'est pas du JSON");

        PiperInstallation found = Assert.IsType<PiperInstallation>(PiperInstallation.Locate(tree.Path));

        Assert.Equal("?", Assert.Single(found.Voices()).Language);
    }

    [Fact]
    public void Une_voix_inconnue_n_a_pas_de_modele()
    {
        using TempTree tree = new();
        tree.Write("piper.exe", "binaire d'essai");
        tree.Write(Path.Combine("voices", "fr_FR-tom-medium.onnx"), "modèle d'essai");

        PiperInstallation found = Assert.IsType<PiperInstallation>(PiperInstallation.Locate(tree.Path));

        Assert.Null(found.ModelPath("voix.qui.n.existe.pas"));
        Assert.NotNull(found.ModelPath(null));
    }

    /// <summary>Arborescence jetable, effacée quoi qu'il arrive.</summary>
    private sealed class TempTree : IDisposable
    {
        public TempTree()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"optimus-piper-essai-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string relative, string content)
        {
            string full = System.IO.Path.Combine(Path, relative);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Un dossier temporaire qui survit n'est pas un echec d'essai.
            }
        }
    }
}
