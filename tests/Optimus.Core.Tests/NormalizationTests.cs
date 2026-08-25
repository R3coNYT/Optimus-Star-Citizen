using Optimus.Core.Intent;

namespace Optimus.Core.Tests;

/// <summary>
/// Normalisation du texte.
///
/// Ces tests existent à cause d'un bug réel : <c>InvariantGlobalization</c> activé dans les
/// propriétés de build désactivait ICU, <c>string.Normalize</c> cessait de décomposer, et
/// « lumières » devenait « lumi res ». Le matcher résolvait alors « allume les lumières » vers
/// <c>ship.weapons.toggle</c> — une commande que personne n'avait demandée.
///
/// Un moteur qui envoie des touches n'a pas le droit de se tromper de commande sur un accent.
/// D'où cette batterie, qui échouerait immédiatement si le réglage revenait.
/// </summary>
public sealed class NormalizationTests
{
    [Theory]
    [InlineData("lumières", "lumieres")]
    [InlineData("prépare", "prepare")]
    [InlineData("système", "systeme")]
    [InlineData("boucliers à l'avant", "boucliers a l avant")]
    [InlineData("éteins", "eteins")]
    [InlineData("Décollage", "decollage")]
    [InlineData("ÉJECTION", "ejection")]
    [InlineData("çà et là", "ca et la")]
    [InlineData("naïve", "naive")]
    [InlineData("coûte", "coute")]
    public void Les_accents_sont_replies_et_non_supprimes(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("Optimus, allume les lumières !", "optimus allume les lumieres")]
    [InlineData("ouvre-moi les portes", "ouvre moi les portes")]
    [InlineData("  DOUBLE   espace  ", "double espace")]
    public void La_ponctuation_et_la_casse_disparaissent(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.Normalize(input));
    }

    [Fact]
    public void Le_mot_d_eveil_est_retire_en_tete()
    {
        string normalized = TextNormalizer.Normalize("Optimus, ouvre les portes");
        Assert.Equal("ouvre les portes", TextNormalizer.StripWakeWord(normalized, "Optimus"));
    }

    [Theory]
    [InlineData("optimus ouvre les portes")]
    [InlineData("optimuss ouvre les portes")]
    [InlineData("optimis ouvre les portes")]
    [InlineData("ok optimus ouvre les portes")]
    [InlineData("hey optimus ouvre les portes")]
    public void Le_mot_d_eveil_tolere_une_transcription_approximative(string utterance)
    {
        // Le spike S0-2 a reconnu « Optimus » sur les 48 mesures, mais une finale mangee ou un
        // « ok » de politesse ne doivent pas empecher la commande de passer.
        string normalized = TextNormalizer.Normalize(utterance);
        Assert.Equal("ouvre les portes", TextNormalizer.StripWakeWord(normalized, "Optimus"));
    }

    [Theory]
    [InlineData("optique ouvre les portes")]
    [InlineData("optimisme ouvre les portes")]
    public void Un_mot_seulement_voisin_n_est_pas_pris_pour_le_mot_d_eveil(string utterance)
    {
        // Le pendant du test precedent, et il compte autant : une tolerance trop large ferait
        // prendre n'importe quel mot commencant par « opti » pour un appel au copilote.
        // La borne est fixee a deux modifications pour un mot de sept lettres.
        string normalized = TextNormalizer.Normalize(utterance);
        Assert.Equal(normalized, TextNormalizer.StripWakeWord(normalized, "Optimus"));
    }

    [Fact]
    public void Un_mot_d_eveil_absent_laisse_la_phrase_intacte()
    {
        string normalized = TextNormalizer.Normalize("ouvre les portes");
        Assert.Equal("ouvre les portes", TextNormalizer.StripWakeWord(normalized, "Optimus"));
    }

    [Fact]
    public void Les_mots_parasites_sont_ecartes()
    {
        Assert.Equal("ouvre les portes", TextNormalizer.Normalize("euh ouvre les portes stp"));
    }

    [Fact]
    public void Un_article_n_est_pas_confondu_avec_un_nombre()
    {
        // « un » etait converti en « 1 », ce qui degradait la comparaison au lieu de l'aider :
        // en francais c'est bien plus souvent un article qu'une quantite.
        Assert.Equal("fais un rapport", TextNormalizer.Normalize("fais un rapport"));
        Assert.Equal("monte de 3 crans", TextNormalizer.Normalize("monte de trois crans"));
    }
}
