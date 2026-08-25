using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Optimus.Spike
{
    /// <summary>Description de la fenêtre actuellement au premier plan.</summary>
    public sealed class ForegroundInfo
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string ProcessName;
        public string WindowTitle;

        public override string ToString()
        {
            return string.Format("{0} (pid {1}) — \"{2}\"", ProcessName, ProcessId, WindowTitle);
        }
    }

    /// <summary>
    /// Localisation du processus cible et vérifications de contexte (premier plan, élévation).
    /// Aucun chemin d'installation n'est codé en dur : on part toujours du processus.
    /// </summary>
    public static class TargetLocator
    {
        /// <summary>Noms de processus candidats pour Star Citizen (sans extension).</summary>
        public static readonly string[] StarCitizenProcessNames = { "StarCitizen", "StarCitizen_Launcher", "RSI Launcher" };

        public static Process FindProcess(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            return processes.Length > 0 ? processes[0] : null;
        }

        /// <summary>Cherche Star Citizen parmi les noms connus. Retourne null si absent.</summary>
        public static Process FindStarCitizen()
        {
            for (int i = 0; i < StarCitizenProcessNames.Length; i++)
            {
                Process p = FindProcess(StarCitizenProcessNames[i]);
                if (p != null) return p;
            }
            return null;
        }

        public static ForegroundInfo GetForeground()
        {
            ForegroundInfo info = new ForegroundInfo();
            info.Handle = Interop.GetForegroundWindow();
            if (info.Handle == IntPtr.Zero)
            {
                info.ProcessName = "(aucune)";
                info.WindowTitle = "";
                return info;
            }

            uint pid;
            Interop.GetWindowThreadProcessId(info.Handle, out pid);
            info.ProcessId = pid;

            StringBuilder title = new StringBuilder(512);
            Interop.GetWindowText(info.Handle, title, title.Capacity);
            info.WindowTitle = title.ToString();

            try
            {
                Process p = Process.GetProcessById((int)pid);
                info.ProcessName = p.ProcessName;
            }
            catch (Exception)
            {
                info.ProcessName = "(inconnu)";
            }

            return info;
        }

        /// <summary>Le processus donné est-il au premier plan ?</summary>
        public static bool IsForeground(Process process)
        {
            if (process == null) return false;
            ForegroundInfo fg = GetForeground();
            return fg.ProcessId == (uint)process.Id;
        }

        public static bool IsCurrentProcessElevated()
        {
            try
            {
                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Le processus cible est-il élevé ? Renvoie null si l'information est inaccessible —
        /// ce qui est en soi un signal fort d'élévation (UIPI bloquera alors l'injection).
        /// </summary>
        public static bool? IsProcessElevated(Process process)
        {
            if (process == null) return null;

            IntPtr handle = Interop.OpenProcess(Interop.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)process.Id);
            if (handle == IntPtr.Zero) return null;

            IntPtr token = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (!Interop.OpenProcessToken(handle, Interop.TOKEN_QUERY, out token)) return null;

                buffer = Marshal.AllocHGlobal(sizeof(int));
                uint returned;
                if (!Interop.GetTokenInformation(token, Interop.TokenElevation, buffer, sizeof(int), out returned))
                    return null;

                return Marshal.ReadInt32(buffer) != 0;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) Interop.CloseHandle(token);
                Interop.CloseHandle(handle);
            }
        }
    }
}
