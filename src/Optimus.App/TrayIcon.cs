using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Optimus.App;

/// <summary>
/// L'icône de la zone de notification, en Win32 direct.
///
/// <b>Pourquoi pas <c>NotifyIcon</c> de WinForms.</b> Il ferait le travail en dix lignes, mais
/// activer <c>UseWindowsForms</c> ajoute l'ensemble de WinForms à une publication autonome qui
/// pèse déjà 76 Mo. Une dizaine de mégaoctets imposés à tout le monde pour une icône de seize
/// pixels : c'est le calcul qui a fait renoncer à embarquer Piper et le banc d'essai, il vaut
/// aussi ici. Le reste de l'application appelle déjà <c>SendInput</c> et <c>dwmapi</c> ; une
/// dépendance de plus serait la seule chose nouvelle.
///
/// <b>La fenêtre invisible.</b> <c>Shell_NotifyIcon</c> ne sait pas rappeler une application,
/// seulement une fenêtre. Il en faut donc une, et elle est créée en <c>HWND_MESSAGE</c> : sans
/// surface, sans place dans la barre des tâches, sans existence pour l'utilisateur.
///
/// <b>Le message de l'explorateur.</b> Quand l'explorateur Windows redémarre — ce qui arrive —
/// il oublie toutes les icônes de la zone de notification et diffuse <c>TaskbarCreated</c> pour
/// qu'on les repose. Sans cette écoute, Optimus continuerait de tourner sans plus rien montrer,
/// et le pilote conclurait qu'il s'est arrêté.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int Callback = 0x8000 + 1;   // WM_APP + 1

    private const int NimAdd = 0;
    private const int NimModify = 1;
    private const int NimDelete = 2;

    private const int NifMessage = 0x01;
    private const int NifIcon = 0x02;
    private const int NifTip = 0x04;
    private const int NifInfo = 0x10;

    private const int WmLeftButtonUp = 0x0202;
    private const int WmLeftDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;

    private const int HwndMessage = -3;

    private readonly HwndSource _window;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;

    private string _tooltip;

    private bool _placed;
    private bool _disposed;

    /// <summary>Clic gauche : le pilote veut revoir la fenêtre.</summary>
    public event EventHandler? Opened;

    /// <summary>Clic droit : il veut le menu.</summary>
    public event EventHandler? MenuRequested;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;

        HwndSourceParameters parameters = new("Optimus.Tray")
        {
            ParentWindow = HwndMessage,
            Width = 0,
            Height = 0,
        };

        _window = new HwndSource(parameters);
        _window.AddHook(OnMessage);

        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _icon = LoadOwnIcon();

        Place(NimAdd);
    }

    /// <summary>
    /// Change ce que dit l'infobulle.
    ///
    /// C'est le seul moyen de savoir, fenêtre fermée, si Optimus écoute encore. L'icône vit
    /// souvent dans le débordement caché de la barre des tâches : on ne la voit qu'en allant
    /// la chercher, et alors il faut qu'elle réponde autre chose que son nom.
    /// </summary>
    public void SetTooltip(string tooltip)
    {
        if (_disposed || tooltip == _tooltip)
        {
            return;
        }

        _tooltip = tooltip;

        NotifyIconData data = Describe();
        data.Flags = NifTip;

        Shell_NotifyIconW(NimModify, ref data);
    }

    /// <summary>
    /// Une bulle, pour dire ce que la fermeture de la fenêtre ne dit pas.
    ///
    /// Windows peut la refuser — mode « ne pas déranger », notifications coupées pour
    /// l'application. L'appel échoue alors sans bruit, et c'est acceptable : la bulle informe,
    /// elle ne décide de rien.
    /// </summary>
    public void ShowBalloon(string title, string body)
    {
        if (_disposed)
        {
            return;
        }

        NotifyIconData data = Describe();
        data.Flags = NifInfo;
        data.InfoTitle = Trim(title, 63);
        data.Info = Trim(body, 255);
        data.InfoFlags = 0x01;   // NIIF_INFO

        Shell_NotifyIconW(NimModify, ref data);
    }

    private nint OnMessage(nint window, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message == _taskbarCreated)
        {
            // L'explorateur a redemarre : l'icone n'existe plus nulle part, on la repose.
            _placed = false;
            Place(NimAdd);
            handled = true;
            return 0;
        }

        if (message != Callback)
        {
            return 0;
        }

        switch ((int)lparam)
        {
            case WmLeftButtonUp:
            case WmLeftDoubleClick:
                Opened?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;

            case WmRightButtonUp:
                MenuRequested?.Invoke(this, EventArgs.Empty);
                handled = true;
                break;
        }

        return 0;
    }

    private void Place(int action)
    {
        NotifyIconData data = Describe();
        data.Flags = NifMessage | NifIcon | NifTip;

        if (Shell_NotifyIconW(action, ref data))
        {
            _placed = true;
        }
    }

    private NotifyIconData Describe() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(),
        Window = _window.Handle,
        Id = 1,
        CallbackMessage = Callback,
        Icon = _icon,
        Tip = Trim(_tooltip, 127),
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    /// <summary>
    /// L'icône de l'exécutable, ou celle du système à défaut.
    ///
    /// La prendre sur le fichier plutôt que de la charger en ressource évite d'en tenir deux
    /// copies : c'est déjà celle que Windows montre dans la barre des tâches et sur le bureau.
    /// </summary>
    private static nint LoadOwnIcon()
    {
        string? executable = Environment.ProcessPath;

        if (executable is not null)
        {
            nint[] small = new nint[1];

            if (ExtractIconExW(executable, 0, null, small, 1) > 0 && small[0] != 0)
            {
                return small[0];
            }
        }

        return LoadIconW(0, 32512);   // IDI_APPLICATION
    }

    /// <summary>Coupe à la longueur que la structure accepte, accent compris.</summary>
    private static string Trim(string text, int maximum) =>
        text.Length <= maximum ? text : text[..maximum];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_placed)
        {
            // Sans ce retrait, l'icone survit a l'application : elle reste affichee jusqu'a ce
            // qu'on passe la souris dessus, et le pilote croit Optimus encore en marche.
            NotifyIconData data = Describe();
            Shell_NotifyIconW(NimDelete, ref data);
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
        }

        _window.RemoveHook(OnMessage);
        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public nint Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public nint Icon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public int State;
        public int StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public int Version;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public int InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconExW(string file, int index, nint[]? large, nint[]? small, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadIconW(nint instance, int name);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
