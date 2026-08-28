using Optimus.Core.Speech;
using Optimus.Infrastructure.Speech;

namespace Optimus.Infrastructure.Tests;

/// <summary>
/// L'étage de parole libre.
///
/// Deux natures d'essais ici, et la distinction compte. Les réglages et la détection
/// d'installation s'éprouvent partout, sans rien installer. La transcription elle-même exige
/// whisper.cpp et un modèle de 150 Mo : ces essais-là <b>s'effacent</b> quand l'installation
/// n'est pas là, plutôt que d'échouer. Un essai rouge doit signaler un défaut, jamais une
/// dépendance absente — sans quoi on prend l'habitude d'ignorer le rouge.
/// </summary>
public sealed class WhisperTests : IDisposable
{
    private readonly string _root;

    public WhisperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"optimus-whisper-{Guid.NewGuid():N}");
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

    // ---------------------------------------------------------------------------- reglages

    /// <summary>
    /// Éteint par défaut, et c'est une promesse, pas un hasard.
    ///
    /// Tant que l'étage n'est pas demandé, rien de ce que dit le pilote n'est transcrit — ce que
    /// l'écran de réglages affiche depuis l'origine.
    /// </summary>
    [Fact]
    public void L_etage_est_eteint_par_defaut()
    {
        Assert.Equal(WhisperMode.Off, new WhisperSettings().Mode);
        Assert.False(WhisperSettings.Disabled.Enabled);
    }

    [Theory]
    [InlineData(WhisperMode.Rejected)]
    [InlineData(WhisperMode.Always)]
    public void Les_deux_autres_positions_montent_l_etage(WhisperMode mode)
    {
        Assert.True(new WhisperSettings(mode).Enabled);
    }

    /// <summary>
    /// La fenêtre réduite vaut 768, jamais 512.
    ///
    /// À 512 le taux d'erreur passe à 31 % (S0-2) : ce n'est plus une transcription, c'est du
    /// bruit. Les 90 ms gagnées ne valent pas ce prix, et le réglage ne le propose donc pas.
    /// </summary>
    [Fact]
    public void La_fenetre_reduite_ne_descend_jamais_a_512()
    {
        Assert.Equal(0, new WhisperSettings(TrimContext: false).AudioContext);
        Assert.Equal(768, new WhisperSettings(TrimContext: true).AudioContext);
    }

    /// <summary>Zéro fil signifie « autant que de processeurs » : le SMT aide (D26).</summary>
    [Fact]
    public void Zero_fil_signifie_tous_les_processeurs()
    {
        Assert.Equal(Environment.ProcessorCount, new WhisperSettings().EffectiveThreads);
        Assert.Equal(8, new WhisperSettings(Threads: 8).EffectiveThreads);
    }

    // ------------------------------------------------------------------------ installation

    [Fact]
    public void Sans_binaire_il_n_y_a_pas_d_installation()
    {
        Assert.Null(WhisperInstallation.Locate(_root));
    }

    /// <summary>
    /// Un binaire sans modèle est une installation à moitié faite.
    ///
    /// La retenir rendrait Optimus muet sur la parole libre sans que rien ne l'explique — même
    /// raison que pour Piper (D57).
    /// </summary>
    [Fact]
    public void Un_binaire_sans_modele_ne_compte_pas()
    {
        File.WriteAllText(Path.Combine(_root, "whisper-cli.exe"), "binaire d'essai");

        Assert.Null(WhisperInstallation.Locate(_root));
    }

    [Fact]
    public void Le_binaire_et_un_modele_suffisent()
    {
        Seed("base", "small");

        WhisperInstallation found = Assert.IsType<WhisperInstallation>(
            WhisperInstallation.Locate(_root));

        Assert.Equal(["base", "small"], found.Models());
    }

    [Fact]
    public void Un_modele_inconnu_retombe_sur_le_premier_installe()
    {
        Seed("base");

        WhisperInstallation found = Assert.IsType<WhisperInstallation>(
            WhisperInstallation.Locate(_root));

        Assert.EndsWith("ggml-base.bin", found.ModelPath("gigantesque"), StringComparison.Ordinal);
        Assert.EndsWith("ggml-base.bin", found.ModelPath(null), StringComparison.Ordinal);
    }

    private void Seed(params string[] models)
    {
        File.WriteAllText(Path.Combine(_root, "whisper-cli.exe"), "binaire d'essai");

        string directory = Path.Combine(_root, "models");
        Directory.CreateDirectory(directory);

        foreach (string model in models)
        {
            File.WriteAllText(Path.Combine(directory, $"ggml-{model}.bin"), "modèle d'essai");
        }
    }

    // ------------------------------------------------- transcription reelle, si Whisper est la

    /// <summary>
    /// La transcription contre le vrai binaire, sur un WAV fabriqué pour l'occasion.
    ///
    /// S'efface si Whisper n'est pas installé : la dépendance pèse 150 Mo et ne peut pas être
    /// exigée d'une machine de compilation.
    /// </summary>
    [SkippableFact]
    public async Task Un_enonce_est_transcrit_par_le_vrai_moteur()
    {
        WhisperInstallation? installation = WhisperInstallation.Locate();

        Skip.If(installation is null, "whisper.cpp n'est pas installé sur cette machine.");

        string wave = Path.Combine(_root, "essai.wav");
        Sine(wave);

        WhisperTranscriber transcriber = new(installation!, new WhisperSettings(WhisperMode.Rejected));

        Transcription heard = await transcriber.TranscribeAsync(wave);

        // Un signal pur ne contient aucune parole : ce qui compte n'est pas le texte rendu mais
        // que l'appel aboutisse, mesure a l'appui, sans lever ni rester bloque.
        Assert.True(heard.ElapsedMs > 0, "la transcription n'a pas été mesurée");
    }

    /// <summary>Un fichier absent ne doit pas lever : un étage facultatif ne fait rien tomber.</summary>
    [SkippableFact]
    public async Task Un_audio_absent_rend_une_transcription_vide()
    {
        WhisperInstallation? installation = WhisperInstallation.Locate();

        Skip.If(installation is null, "whisper.cpp n'est pas installé sur cette machine.");

        WhisperTranscriber transcriber = new(installation!, new WhisperSettings(WhisperMode.Rejected));

        Transcription heard = await transcriber.TranscribeAsync(
            Path.Combine(_root, "n-existe-pas.wav"));

        Assert.False(heard.HasText);
    }

    /// <summary>Écrit un WAV 16 kHz mono d'une seconde : le format que le moteur Windows rend.</summary>
    private static void Sine(string path)
    {
        const int rate = 16000;
        const int samples = rate;

        using BinaryWriter writer = new(File.Create(path));

        writer.Write("RIFF"u8);
        writer.Write(36 + (samples * 2));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);

        for (int i = 0; i < samples; i++)
        {
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
        }
    }
}
