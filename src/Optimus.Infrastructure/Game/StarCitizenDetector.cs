using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Optimus.Core.Abstractions;

namespace Optimus.Infrastructure.Game;

/// <summary>
/// Détection de Star Citizen par son processus.
///
/// Jamais par un chemin d'installation en dur : le jeu se trouve chez l'un dans
/// <c>C:\Program Files\Roberts Space Industries</c>, chez l'autre dans
/// <c>D:\app\80-Star Citizen\…</c>. L'exécutable du processus donne le chemin, et le chemin
/// donne le canal — c'est aussi ainsi qu'on retrouvera <c>Data.p4k</c> et les profils de touches.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class StarCitizenDetector : IGameDetector
{
    private static readonly string[] ProcessNames = ["StarCitizen"];

    public string GameName => "Star Citizen";

    public GameStatus Detect()
    {
        Process? game = FindProcess();
        if (game is null)
        {
            return GameStatus.NotRunning;
        }

        using (game)
        {
            string? executablePath = TryGetPath(game);

            return new GameStatus(
                IsRunning: true,
                IsForeground: IsForeground(game.Id),
                ProcessId: game.Id,
                ExecutablePath: executablePath,
                Channel: ExtractChannel(executablePath),
                IsElevated: TryDetectElevation(game));
        }
    }

    /// <summary>Dossier du canal (<c>…\StarCitizen\LIVE</c>), d'où l'on atteint <c>Data.p4k</c> et les mappings.</summary>
    public static string? ResolveChannelDirectory(string? executablePath)
    {
        // …\StarCitizen\LIVE\Bin64\StarCitizen.exe  ->  …\StarCitizen\LIVE
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        DirectoryInfo? bin = Directory.GetParent(executablePath);
        return bin?.Parent?.FullName;
    }

    private static Process? FindProcess()
    {
        foreach (string name in ProcessNames)
        {
            Process[] found = Process.GetProcessesByName(name);
            if (found.Length == 0)
            {
                continue;
            }

            for (int i = 1; i < found.Length; i++)
            {
                found[i].Dispose();
            }

            return found[0];
        }

        return null;
    }

    /// <summary>
    /// Chemin de l'exécutable du jeu.
    ///
    /// <c>Process.MainModule</c> échoue sur Star Citizen : il exige des droits que l'anti-triche
    /// refuse, et renvoie « accès refusé » alors même que le jeu n'est pas élevé — constaté sur
    /// R3CON-PC le 2026-08-25. <c>QueryFullProcessImageName</c> est l'API prévue pour ce cas :
    /// elle se contente de PROCESS_QUERY_LIMITED_INFORMATION.
    ///
    /// Ce chemin n'est pas cosmétique : c'est de lui qu'on déduit le canal, puis l'emplacement
    /// de <c>Data.p4k</c> et des profils de touches à importer.
    /// </summary>
    private static string? TryGetPath(Process process)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Id);
        if (handle == 0)
        {
            return null;
        }

        try
        {
            int capacity = 1024;
            char[] buffer = new char[capacity];

            if (QueryFullProcessImageName(handle, 0, buffer, ref capacity) && capacity > 0)
            {
                return new string(buffer, 0, capacity);
            }
        }
        finally
        {
            CloseHandle(handle);
        }

        // Dernier recours : l'API classique, qui fonctionne pour les processus ordinaires.
        try
        {
            return process.MainModule?.FileName;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ExtractChannel(string? executablePath)
    {
        string? channelDirectory = ResolveChannelDirectory(executablePath);
        if (channelDirectory is null)
        {
            return null;
        }

        string candidate = Path.GetFileName(channelDirectory);
        return candidate.Length == 0 ? null : candidate;
    }

    private static bool? TryDetectElevation(Process process)
    {
        nint handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)process.Id);
        if (handle == 0)
        {
            // Impossible d'ouvrir le processus : signal probable d'élévation.
            return null;
        }

        nint token = 0;

        try
        {
            if (!OpenProcessToken(handle, TokenQuery, out token))
            {
                return null;
            }

            Span<int> buffer = stackalloc int[1];

            unsafe
            {
                fixed (int* pointer = buffer)
                {
                    if (!GetTokenInformation(token, TokenElevation, (nint)pointer, sizeof(int), out _))
                    {
                        return null;
                    }
                }
            }

            return buffer[0] != 0;
        }
        finally
        {
            if (token != 0)
            {
                CloseHandle(token);
            }

            CloseHandle(handle);
        }
    }

    /// <summary>Optimus tourne-t-il en administrateur.</summary>
    public static bool IsCurrentProcessElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsForeground(int processId)
    {
        nint window = GetForegroundWindow();
        if (window == 0)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out uint foregroundPid);
        return foregroundPid == (uint)processId;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageName(nint process, uint flags, [Out] char[] name, ref int size);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint token, int informationClass, nint information, uint length, out uint returnLength);
}
