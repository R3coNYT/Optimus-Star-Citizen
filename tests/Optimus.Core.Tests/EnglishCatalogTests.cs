using Optimus.Core.Domain.Commands;
using Optimus.Core.Intent;
using Optimus.Core.Loading;
using Optimus.Core.Localization;

namespace Optimus.Core.Tests;

/// <summary>
/// Le catalogue anglais tient les mêmes promesses que le français.
///
/// Défaut mesuré le 2026-08-29 : le banc d'essai lisait le catalogue français en dur, et
/// répondait « I do not know that command » à toutes les phrases anglaises. La réponse
/// étant elle-même en anglais, rien ne trahissait la cause — on croyait la traduction bonne
/// et le pilote maladroit.
///
/// Ces essais ferment la porte des deux côtés : les phrases anglaises résolvent, et les deux
/// catalogues décrivent les mêmes commandes. Un fichier anglais qui prendrait du retard sur
/// le français ne se verrait autrement qu'à l'usage, une commande à la fois.
/// </summary>
public sealed class EnglishCatalogTests
{
    private readonly CommandCatalog _english;

    public EnglishCatalogTests()
    {
        string directory = Path.Combine(TestData.RepositoryRoot, "data", "commands");
        string? path = Language.Localized(directory, "starcitizen.core", ".json", Language.English);

        // Résoudre par la même fonction que le produit, et non par un chemin écrit ici :
        // autrement l'essai passerait alors même que la résolution serait cassée.
        Assert.NotNull(path);
        Assert.EndsWith(".en.json", path);

        _english = JsonCatalogLoader.LoadCatalog(path!).Value;
    }

    [Theory]
    [InlineData("Optimus, lights on", "ship.lights.toggle", CommandPolarity.On)]
    [InlineData("Optimus, turn off the lights", "ship.lights.toggle", CommandPolarity.Off)]
    [InlineData("Optimus, lights", "ship.lights.toggle", CommandPolarity.Neutral)]
    [InlineData("Optimus, open the doors", "ship.doors.toggle", CommandPolarity.On)]
    [InlineData("Optimus, combat mode", "nav.master_mode.cycle", CommandPolarity.On)]
    [InlineData("Optimus, gear down", "flight.landing_gear.toggle", CommandPolarity.On)]
    [InlineData("Optimus, retract the gear", "flight.landing_gear.toggle", CommandPolarity.Off)]
    public void Une_phrase_anglaise_resout_exactement(
        string utterance, string commandId, CommandPolarity expected)
    {
        FastIntentMatcher matcher = new(_english);

        IntentResolution resolution = matcher.Resolve(utterance, wakeWord: "Optimus");

        Assert.Equal(commandId, resolution.Best!.Command.Id);
        Assert.Equal(expected, resolution.Best.Polarity);
        Assert.Equal(IntentDecision.Execute, resolution.Decision);

        // « Exact » et non « Fuzzy » : c'est toute la différence entre une phrase reconnue et
        // une phrase que le rapprochement approximatif a rattrapée par chance. Le défaut se
        // manifestait justement par des scores de 0,47.
        Assert.Equal(MatchKind.Exact, resolution.Best.Kind);
    }

    [Fact]
    public void Les_deux_catalogues_decrivent_les_memes_commandes()
    {
        CommandCatalog french = JsonCatalogLoader.LoadCatalog(
            Path.Combine(TestData.RepositoryRoot, "data", "commands", "starcitizen.core.json")).Value;

        string[] missing = french.Commands.Select(c => c.Id)
            .Except(_english.Commands.Select(c => c.Id)).ToArray();
        string[] extra = _english.Commands.Select(c => c.Id)
            .Except(french.Commands.Select(c => c.Id)).ToArray();

        Assert.True(missing.Length == 0, $"absentes du catalogue anglais : {string.Join(", ", missing)}");
        Assert.True(extra.Length == 0, $"absentes du catalogue français : {string.Join(", ", extra)}");
    }

    [Fact]
    public void Aucune_commande_anglaise_n_est_muette()
    {
        // Une commande sans phrase est inatteignable par la voix. Elle existerait au
        // catalogue, s'afficherait à l'écran, et ne répondrait jamais.
        foreach (CommandDefinition command in _english.Commands)
        {
            Assert.True(
                command.VoicePhrases.Count + command.PhrasesOn.Count + command.PhrasesOff.Count > 0,
                $"« {command.Id} » n'a aucune formulation anglaise");
        }
    }
}
