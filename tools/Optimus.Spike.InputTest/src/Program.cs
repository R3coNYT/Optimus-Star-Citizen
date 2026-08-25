using System;

namespace Optimus.Spike
{
    /// <summary>
    /// Point d'entrée pour l'exécution via le SDK .NET.
    /// L'exécution sans SDK (PowerShell + Add-Type) appelle directement
    /// <see cref="SpikeRunner"/>.Run — voir run-spike.ps1.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                return SpikeRunner.Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERREUR FATALE : " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }
    }
}
