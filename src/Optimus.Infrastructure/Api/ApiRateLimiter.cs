namespace Optimus.Infrastructure.Api;

/// <summary>
/// Plafond glissant d'exécutions, par client.
///
/// Ce n'est pas un souci de charge — la machine encaisserait mille requêtes par seconde — mais
/// de vaisseau : trente commandes en rafale sont déjà ingérables, et un client qui boucle ne
/// doit pas pouvoir marteler le clavier du pilote pendant qu'il vole.
///
/// Fenêtre glissante plutôt que compteur remis à zéro chaque minute : un compteur périodique
/// laisse passer deux fois le plafond à cheval sur la bascule, ce qui est exactement le moment
/// où un client emballé fait le plus de dégâts.
/// </summary>
/// <param name="perMinute">Exécutions autorisées par minute et par client.</param>
/// <param name="clock">
/// Horloge, en millisecondes. Injectable pour que l'expiration de la fenêtre s'éprouve sans
/// attendre une minute — un essai qui dort soixante secondes finit par ne plus être joué.
/// </param>
internal sealed class ApiRateLimiter(int perMinute, Func<long>? clock = null)
{
    private const long WindowMs = 60_000;

    private readonly Dictionary<string, Queue<long>> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private readonly object _gate = new();

    /// <summary>Vrai si le client peut exécuter maintenant. L'appel consomme un jeton.</summary>
    public bool Allow(string client)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(client);

        long now = _clock();

        lock (_gate)
        {
            if (!_seen.TryGetValue(client, out Queue<long>? window))
            {
                window = new Queue<long>();
                _seen[client] = window;
            }

            while (window.Count > 0 && now - window.Peek() >= WindowMs)
            {
                window.Dequeue();
            }

            if (window.Count >= perMinute)
            {
                return false;
            }

            window.Enqueue(now);
            return true;
        }
    }
}
