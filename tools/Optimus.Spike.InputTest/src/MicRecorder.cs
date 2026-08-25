using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Optimus.Spike
{
    /// <summary>
    /// Capture microphone via l'API waveIn (winmm), en PCM 16 kHz mono 16 bits - exactement le
    /// format attendu par Whisper.
    ///
    /// Pourquoi waveIn et non WASAPI : le spike doit tourner sans SDK ni dépendance NuGet, et
    /// waveIn se pilote en P/Invoke pur. La question posée par S0-3 est « peut-on capturer le
    /// micro pendant que le jeu tourne, et en combien de temps la capture démarre-t-elle ». Une
    /// réponse positive en waveIn vaut a fortiori pour WASAPI en mode partagé, qui sera utilisé
    /// en production (NAudio) et offre une latence inférieure.
    /// </summary>
    public sealed class MicRecorder : IDisposable
    {
        public const int SampleRate = 16000;
        public const int Channels = 1;
        public const int BitsPerSample = 16;

        private const int BufferCount = 8;
        private const int BufferMs = 100;

        private IntPtr _handle = IntPtr.Zero;
        private IntPtr[] _headers;
        private IntPtr[] _buffers;
        private Thread _pollThread;
        private volatile bool _recording;
        private MemoryStream _captured;
        private readonly object _sync = new object();

        /// <summary>Horodatage haute résolution du premier échantillon réellement reçu.</summary>
        public long FirstSampleTicks { get; private set; }

        /// <summary>Niveau crête observé (0..1).</summary>
        public double PeakLevel { get; private set; }

        /// <summary>Niveau RMS moyen (0..1).</summary>
        public double RmsLevel { get; private set; }

        public string LastError { get; private set; }

        public static List<string> ListDevices()
        {
            List<string> devices = new List<string>();
            uint count = WaveNative.waveInGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                WaveNative.WAVEINCAPS caps = new WaveNative.WAVEINCAPS();
                if (WaveNative.waveInGetDevCaps((IntPtr)i, ref caps,
                        (uint)Marshal.SizeOf(typeof(WaveNative.WAVEINCAPS))) == 0)
                {
                    devices.Add(caps.szPname);
                }
            }
            return devices;
        }

        /// <summary>Ouvre le périphérique et démarre la capture. Retourne false en cas d'échec.</summary>
        public bool Start(int deviceIndex)
        {
            lock (_sync)
            {
                if (_recording) return true;

                _captured = new MemoryStream();
                PeakLevel = 0;
                RmsLevel = 0;
                FirstSampleTicks = 0;
                LastError = null;

                WaveNative.WAVEFORMATEX format = new WaveNative.WAVEFORMATEX();
                format.wFormatTag = WaveNative.WAVE_FORMAT_PCM;
                format.nChannels = (ushort)Channels;
                format.nSamplesPerSec = SampleRate;
                format.wBitsPerSample = BitsPerSample;
                format.nBlockAlign = (ushort)(Channels * BitsPerSample / 8);
                format.nAvgBytesPerSec = (uint)(SampleRate * format.nBlockAlign);
                format.cbSize = 0;

                IntPtr device = deviceIndex < 0 ? WaveNative.WAVE_MAPPER : (IntPtr)deviceIndex;

                int result = WaveNative.waveInOpen(out _handle, device, ref format,
                    IntPtr.Zero, IntPtr.Zero, WaveNative.CALLBACK_NULL);
                if (result != 0)
                {
                    LastError = "waveInOpen a échoué (code " + result + ") - périphérique occupé ou format refusé";
                    return false;
                }

                int bufferSize = SampleRate * Channels * (BitsPerSample / 8) * BufferMs / 1000;
                _headers = new IntPtr[BufferCount];
                _buffers = new IntPtr[BufferCount];

                for (int i = 0; i < BufferCount; i++)
                {
                    _buffers[i] = Marshal.AllocHGlobal(bufferSize);
                    WaveNative.WAVEHDR header = new WaveNative.WAVEHDR();
                    header.lpData = _buffers[i];
                    header.dwBufferLength = (uint)bufferSize;

                    _headers[i] = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WaveNative.WAVEHDR)));
                    Marshal.StructureToPtr(header, _headers[i], false);

                    WaveNative.waveInPrepareHeader(_handle, _headers[i],
                        (uint)Marshal.SizeOf(typeof(WaveNative.WAVEHDR)));
                    WaveNative.waveInAddBuffer(_handle, _headers[i],
                        (uint)Marshal.SizeOf(typeof(WaveNative.WAVEHDR)));
                }

                _recording = true;
                WaveNative.waveInStart(_handle);

                _pollThread = new Thread(PollLoop);
                _pollThread.IsBackground = true;
                _pollThread.Name = "OptimusSpikeMic";
                _pollThread.Start();

                return true;
            }
        }

        /// <summary>
        /// Les tampons remplis sont récupérés par scrutation plutôt que par callback : une
        /// callback waveIn s'exécute dans un contexte contraint où toute exception managée est
        /// fatale. La scrutation à 5 ms est largement suffisante pour des tampons de 100 ms.
        /// </summary>
        private void PollLoop()
        {
            double peak = 0;
            double sumSquares = 0;
            long sampleCount = 0;
            int headerSize = Marshal.SizeOf(typeof(WaveNative.WAVEHDR));

            try
            {
                while (_recording)
                {
                    bool idle = true;

                    for (int i = 0; i < BufferCount && _recording; i++)
                    {
                        WaveNative.WAVEHDR header = (WaveNative.WAVEHDR)
                            Marshal.PtrToStructure(_headers[i], typeof(WaveNative.WAVEHDR));

                        if ((header.dwFlags & WaveNative.WHDR_DONE) == 0) continue;

                        idle = false;
                        int recorded = (int)header.dwBytesRecorded;

                        if (recorded > 0)
                        {
                            if (FirstSampleTicks == 0)
                                FirstSampleTicks = System.Diagnostics.Stopwatch.GetTimestamp();

                            byte[] managed = new byte[recorded];
                            Marshal.Copy(header.lpData, managed, 0, recorded);

                            lock (_sync)
                            {
                                if (_captured != null) _captured.Write(managed, 0, recorded);
                            }

                            for (int s = 0; s + 1 < recorded; s += 2)
                            {
                                double value = BitConverter.ToInt16(managed, s) / 32768.0;
                                double magnitude = Math.Abs(value);
                                if (magnitude > peak) peak = magnitude;
                                sumSquares += value * value;
                                sampleCount++;
                            }
                        }

                        WaveNative.waveInUnprepareHeader(_handle, _headers[i], (uint)headerSize);
                        header.dwFlags = 0;
                        header.dwBytesRecorded = 0;
                        Marshal.StructureToPtr(header, _headers[i], false);
                        WaveNative.waveInPrepareHeader(_handle, _headers[i], (uint)headerSize);
                        WaveNative.waveInAddBuffer(_handle, _headers[i], (uint)headerSize);
                    }

                    if (idle) Thread.Sleep(5);
                }
            }
            catch (Exception ex)
            {
                LastError = "capture interrompue : " + ex.Message;
            }
            finally
            {
                PeakLevel = peak;
                RmsLevel = sampleCount > 0 ? Math.Sqrt(sumSquares / sampleCount) : 0;
            }
        }

        /// <summary>Arrête la capture et renvoie les octets PCM bruts.</summary>
        public byte[] Stop()
        {
            lock (_sync)
            {
                if (!_recording) return new byte[0];
                _recording = false;
            }

            if (_pollThread != null) { _pollThread.Join(1000); _pollThread = null; }

            if (_handle != IntPtr.Zero)
            {
                WaveNative.waveInStop(_handle);
                WaveNative.waveInReset(_handle);

                if (_headers != null)
                {
                    for (int i = 0; i < _headers.Length; i++)
                    {
                        if (_headers[i] == IntPtr.Zero) continue;
                        WaveNative.waveInUnprepareHeader(_handle, _headers[i],
                            (uint)Marshal.SizeOf(typeof(WaveNative.WAVEHDR)));
                        Marshal.FreeHGlobal(_headers[i]);
                        _headers[i] = IntPtr.Zero;
                    }
                }
                if (_buffers != null)
                {
                    for (int i = 0; i < _buffers.Length; i++)
                    {
                        if (_buffers[i] == IntPtr.Zero) continue;
                        Marshal.FreeHGlobal(_buffers[i]);
                        _buffers[i] = IntPtr.Zero;
                    }
                }

                WaveNative.waveInClose(_handle);
                _handle = IntPtr.Zero;
            }

            lock (_sync)
            {
                byte[] data = _captured != null ? _captured.ToArray() : new byte[0];
                if (_captured != null) { _captured.Dispose(); _captured = null; }
                return data;
            }
        }

        /// <summary>Écrit un WAV PCM standard, directement consommable par Whisper.</summary>
        public static void WriteWav(string path, byte[] pcm)
        {
            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(file))
            {
                int byteRate = SampleRate * Channels * BitsPerSample / 8;
                short blockAlign = (short)(Channels * BitsPerSample / 8);

                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + pcm.Length);
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)Channels);
                writer.Write(SampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write((short)BitsPerSample);
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(pcm.Length);
                writer.Write(pcm);
            }
        }

        public static double DurationMs(byte[] pcm)
        {
            return pcm.Length * 1000.0 / (SampleRate * Channels * (BitsPerSample / 8));
        }

        public void Dispose()
        {
            try { Stop(); }
            catch (Exception) { }
        }
    }

    internal static class WaveNative
    {
        public const int WAVE_FORMAT_PCM = 1;
        public const uint CALLBACK_NULL = 0;
        public const uint WHDR_DONE = 0x00000001;
        public static readonly IntPtr WAVE_MAPPER = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll")]
        public static extern uint waveInGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")]
        public static extern int waveInGetDevCaps(IntPtr deviceId, ref WAVEINCAPS caps, uint size);

        [DllImport("winmm.dll")]
        public static extern int waveInOpen(out IntPtr phwi, IntPtr deviceId, ref WAVEFORMATEX format,
            IntPtr callback, IntPtr instance, uint flags);

        [DllImport("winmm.dll")]
        public static extern int waveInPrepareHeader(IntPtr hwi, IntPtr header, uint size);

        [DllImport("winmm.dll")]
        public static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr header, uint size);

        [DllImport("winmm.dll")]
        public static extern int waveInAddBuffer(IntPtr hwi, IntPtr header, uint size);

        [DllImport("winmm.dll")]
        public static extern int waveInStart(IntPtr hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInStop(IntPtr hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInReset(IntPtr hwi);

        [DllImport("winmm.dll")]
        public static extern int waveInClose(IntPtr hwi);
    }
}
