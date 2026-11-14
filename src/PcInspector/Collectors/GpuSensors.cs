using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace PcInspector.Collectors;

/// <summary>
/// Température, ventilateur et charge des cartes graphiques, sans installer de pilote : on lit ce que Windows
/// (WDDM 2.4 et plus, Windows 10 1803+) expose déjà au Gestionnaire des tâches, pour NVIDIA comme AMD.
/// Repli sur nvidia-smi (installé avec le pilote NVIDIA) si Windows ne donne rien.
/// Les puces graphiques intégrées au processeur ne remontent en général pas de température.
/// </summary>
public static class GpuSensors
{
    public sealed record Reading(string Name, double TemperatureC, int? FanRpm);

    public static List<Reading> Read()
    {
        var list = new List<Reading>();
        try { list.AddRange(FromWindows()); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or AccessViolationException or SEHException) { }
        if (list.Count == 0) list.AddRange(FromNvidiaSmi());
        return list;
    }

    /// <summary>Température maximale de toutes les cartes, ou null.</summary>
    public static double? MaxTemperature()
    {
        double? max = null;
        foreach (var r in Read()) if (max is null || r.TemperatureC > max) max = r.TemperatureC;
        return max;
    }

    // ---------- Windows (D3DKMT, comme le Gestionnaire des tâches) ----------

    private const int KMTQAITYPE_ADAPTERREGISTRYINFO = 8;
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;

    private static IEnumerable<Reading> FromWindows()
    {
        var enumArgs = new D3DKMT_ENUMADAPTERS2();
        if (D3DKMTEnumAdapters2(ref enumArgs) != 0 || enumArgs.NumAdapters == 0) yield break;
        var size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        var buffer = Marshal.AllocHGlobal(size * (int)enumArgs.NumAdapters);
        var adapters = new List<D3DKMT_ADAPTERINFO>();
        try
        {
            enumArgs.pAdapters = buffer;
            if (D3DKMTEnumAdapters2(ref enumArgs) != 0) yield break;
            for (var i = 0; i < enumArgs.NumAdapters; i++)
                adapters.Add(Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(buffer + i * size));
        }
        finally { Marshal.FreeHGlobal(buffer); }

        var seen = new HashSet<string>();
        foreach (var adapter in adapters)
        {
            Reading? reading = null;
            try
            {
                var perf = Query<D3DKMT_ADAPTER_PERFDATA>(adapter.hAdapter, KMTQAITYPE_ADAPTERPERFDATA);
                // En dixièmes de degré ; 0 = non fourni par le pilote.
                if (perf is { Temperature: > 0 and < 1500 } p)
                {
                    var name = Query<D3DKMT_ADAPTERREGISTRYINFO>(adapter.hAdapter, KMTQAITYPE_ADAPTERREGISTRYINFO)?.AdapterString?.Trim();
                    if (string.IsNullOrEmpty(name)) name = "Carte graphique";
                    if (seen.Add($"{adapter.AdapterLuidLow}:{adapter.AdapterLuidHigh}"))
                        reading = new Reading(name!, p.Temperature / 10.0, p.FanRPM > 0 ? (int)p.FanRPM : null);
                }
            }
            finally
            {
                var close = new D3DKMT_CLOSEADAPTER { hAdapter = adapter.hAdapter };
                D3DKMTCloseAdapter(ref close);
            }
            if (reading is not null) yield return reading;
        }
    }

    private static T? Query<T>(uint adapter, int type) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var data = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++) Marshal.WriteByte(data, i, 0);
            var query = new D3DKMT_QUERYADAPTERINFO { hAdapter = adapter, Type = type, pPrivateDriverData = data, PrivateDriverDataSize = (uint)size };
            return D3DKMTQueryAdapterInfo(ref query) == 0 ? Marshal.PtrToStructure<T>(data) : null;
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    // ---------- nvidia-smi ----------

    private static IEnumerable<Reading> FromNvidiaSmi()
    {
        var smi = AppPaths.SystemTool("nvidia-smi.exe");
        var list = new List<Reading>();
        if (!File.Exists(smi)) return list;
        ProcessRunner.Run(smi, "--query-gpu=name,temperature.gpu --format=csv,noheader,nounits", line =>
        {
            var parts = line.Split(',');
            if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                list.Add(new Reading(parts[0].Trim(), t, null));
        }, TimeSpan.FromSeconds(10));
        return list;
    }

    // ---------- Windows ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO
    {
        public uint hAdapter;
        public uint AdapterLuidLow;
        public int AdapterLuidHigh;
        public uint NumOfSources;
        public int bPrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
        public uint FanRPM;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct D3DKMT_ADAPTERREGISTRYINFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string AdapterString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string BiosString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DacType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ChipType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 args);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO args);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER args);
}
