namespace Optimus.Infrastructure.Speech;

/// <summary>
/// Windows n'a pas de module vocal pour la langue demandée.
///
/// Une exception à elle seule, et non un <see cref="InvalidOperationException"/> parmi
/// d'autres, parce que ce cas <b>n'est pas une défaillance</b> : il suffit que le pilote ait
/// choisi une langue dont il n'a pas installé la reconnaissance. L'écran doit alors le lui
/// dire et lui indiquer où la prendre — pas afficher un rapport de plantage.
///
/// Elle porte la <b>culture réellement demandée</b>. Le message précédent parlait du français
/// en toutes lettres, y compris quand l'anglais avait été demandé : le pilote allait chercher
/// le mauvais module dans les paramètres de Windows.
/// </summary>
public sealed class MissingRecognizerException(string culture, Exception inner)
    : InvalidOperationException(
        $"Aucun moteur de reconnaissance vocale pour « {culture} ». Vérifiez qu'un microphone "
        + $"est branché et que le module vocal de « {culture} » est installé dans Windows.",
        inner)
{
    /// <summary>La langue qui manque, telle qu'elle a été demandée.</summary>
    public string Culture { get; } = culture;
}
