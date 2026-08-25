using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Optimus.Infrastructure.Input;

/// <summary>
/// Surveille l'état d'une touche de push-to-talk, jeu au premier plan compris.
///
/// La scrutation via <c>GetAsyncKeyState</c> est préférée à <c>RegisterHotKey</c> pour une
/// raison mesurée au spike S0-3 : le raccourci global n'a délivré <b>aucun</b> message, alors
/// que le hook bas niveau voyait chaque appui. Et quand bien même il aurait fonctionné,
/// <c>RegisterHotKey</c> ne signale que l'appui, jamais le relâchement — il ne peut donc pas
/// délimiter une phrase.
///
/// La scrutation, elle, donne l'état réel de la touche à tout instant, sans installer de hook
/// ni intercepter quoi que ce soit : Optimus observe le clavier, il ne s'interpose pas.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PushToTalkWatcher : IDisposable
{
    private readonly ushort _virtualKey;
    private readonly TimeSpan _pollInterval;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _loop;
    private bool _lastState;

    /// <param name="keyName">Nom canonique de la touche, tel qu'il figure dans le profil.</param>
    /// <param name="pollIntervalMs">
    /// Période de scrutation. 15 ms suffit : c'est bien en deçà du temps de réaction humain,
    /// et le coût processeur est négligeable — un appel système par intervalle.
    /// </param>
    public PushToTalkWatcher(string keyName, int pollIntervalMs = 15)
    {
        _virtualKey = ResolveVirtualKey(keyName);
        _pollInterval = TimeSpan.FromMilliseconds(pollIntervalMs);
    }

    /// <summary>La touche est-elle actuellement enfoncée.</summary>
    public bool IsPressed => (GetAsyncKeyState(_virtualKey) & 0x8000) != 0;

    /// <summary>Changement d'état de la touche : vrai à l'appui, faux au relâchement.</summary>
    public event EventHandler<bool>? StateChanged;

    public void Start()
    {
        _loop ??= Task.Run(WatchAsync);
    }

    private async Task WatchAsync()
    {
        _lastState = IsPressed;

        while (!_cancellation.IsCancellationRequested)
        {
            bool current = IsPressed;

            if (current != _lastState)
            {
                _lastState = current;
                StateChanged?.Invoke(this, current);
            }

            try
            {
                await Task.Delay(_pollInterval, _cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Traduit un nom de touche en code de touche virtuelle.
    ///
    /// <c>GetAsyncKeyState</c> raisonne en codes virtuels, pas en scancodes — contrairement à
    /// l'injection, où la table fixe en positions US est impérative. Ici la disposition n'a pas
    /// d'importance : on demande « la touche Inser est-elle enfoncée », pas « quelle touche se
    /// trouve à telle position ».
    /// </summary>
    private static ushort ResolveVirtualKey(string keyName) => keyName.Trim().ToUpperInvariant() switch
    {
        "INSERT" or "INS" => 0x2D,
        "DELETE" or "DEL" => 0x2E,
        "HOME" => 0x24,
        "END" => 0x23,
        "PAGEUP" or "PGUP" => 0x21,
        "PAGEDOWN" or "PGDN" => 0x22,
        "PAUSE" => 0x13,
        "SCROLLLOCK" => 0x91,
        "NUMLOCK" => 0x90,
        "RCTRL" => 0xA3,
        "LCTRL" => 0xA2,
        "RSHIFT" => 0xA1,
        "LSHIFT" => 0xA0,
        "RALT" => 0xA5,
        "LALT" => 0xA4,
        "SPACE" => 0x20,
        "TAB" => 0x09,
        "F13" => 0x7C,
        "F14" => 0x7D,
        "F15" => 0x7E,
        var name when name.Length == 1 && name[0] is >= 'A' and <= 'Z' => (ushort)name[0],
        var name when name.Length == 1 && name[0] is >= '0' and <= '9' => (ushort)name[0],
        var name when name.StartsWith('F') && int.TryParse(name[1..], out int index) && index is >= 1 and <= 12 =>
            (ushort)(0x70 + index - 1),
        _ => throw new ArgumentException($"Touche de push-to-talk non reconnue : « {keyName} ».", nameof(keyName)),
    };

    /// <summary>Vérifie qu'un nom de touche est utilisable, sans lever.</summary>
    public static bool IsSupported(string keyName)
    {
        try
        {
            _ = ResolveVirtualKey(keyName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int virtualKey);

    public void Dispose()
    {
        _cancellation.Cancel();

        try
        {
            _loop?.Wait(500);
        }
        catch (AggregateException)
        {
            // La boucle s'est terminee sur l'annulation : c'est le comportement attendu.
        }

        _cancellation.Dispose();
    }
}
