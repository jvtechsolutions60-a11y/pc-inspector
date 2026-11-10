using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PcMaintenance.Preparation;

/// <summary>
/// Écran de l'acheteur : chaque écran passe à sa résolution la plus haute (avec la meilleure fréquence à cette
/// résolution), enregistrée pour tous les comptes, et la mise à l'échelle est à 100 % dans le profil par défaut
/// (copié pour le compte que l'acheteur créera). Fait en fin de préparation, quand le pilote graphique est installé :
/// avant, le pilote de base de Windows limite souvent la résolution.
/// </summary>
internal static class Display
{
    public static List<string> Run(IProgress<string> log)
    {
        var done = new List<string>();
        foreach (var line in Resolutions())
        {
            log.Report(line);
            done.Add(line);
        }
        if (ScaleTo100(log)) done.Add("Mise à l'échelle à 100 % pour le compte de l'acheteur");
        return done;
    }

    // ---------- Résolution ----------

    private static IEnumerable<string> Resolutions()
    {
        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) yield break;
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var modes = new List<DEVMODE>();
            for (var m = 0; ; m++)
            {
                var mode = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettings(device.DeviceName, m, ref mode)) break;
                if (mode.dmBitsPerPel >= 32) modes.Add(mode);
            }
            if (modes.Count == 0) continue;

            var current = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            EnumDisplaySettings(device.DeviceName, ENUM_CURRENT_SETTINGS, ref current);
            var best = modes.OrderByDescending(d => (long)d.dmPelsWidth * d.dmPelsHeight)
                .ThenByDescending(d => d.dmDisplayFrequency).First();
            var name = $"Écran {i + 1} ({device.DeviceString.Trim()})";
            if (best.dmPelsWidth == current.dmPelsWidth && best.dmPelsHeight == current.dmPelsHeight && best.dmDisplayFrequency == current.dmDisplayFrequency)
            {
                yield return $"{name} : déjà à {best.dmPelsWidth} × {best.dmPelsHeight} ({best.dmDisplayFrequency} Hz)";
                continue;
            }

            best.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL;
            var code = ChangeDisplaySettingsEx(device.DeviceName, ref best, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_GLOBAL, IntPtr.Zero);
            yield return code == DISP_CHANGE_SUCCESSFUL || code == DISP_CHANGE_RESTART
                ? $"{name} : {best.dmPelsWidth} × {best.dmPelsHeight} ({best.dmDisplayFrequency} Hz)" + (code == DISP_CHANGE_RESTART ? ", appliqué au redémarrage" : "")
                : $"{name} : résolution non changée (code {code}), à régler à la main";
        }
    }

    // ---------- Mise à l'échelle ----------

    /// <summary>LogPixels = 96 (100 %) et Win8DpiScaling = 1 : la valeur est imposée au lieu de celle recommandée par Windows.</summary>
    private static bool ScaleTo100(IProgress<string> log)
    {
        const string script = @"
$hive = Join-Path $env:SystemDrive 'Users\Default\NTUSER.DAT'
& reg.exe load 'HKU\JVTECH-Ecran' $hive | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Output 'ERREUR|chargement du profil par défaut impossible'; return }
try {
  foreach ($root in 'Registry::HKEY_USERS\JVTECH-Ecran', 'HKCU:') {
    $path = $root + '\Control Panel\Desktop'
    New-ItemProperty -Path $path -Name 'LogPixels' -Value 96 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $path -Name 'Win8DpiScaling' -Value 1 -PropertyType DWord -Force | Out-Null
  }
  Write-Output 'OK'
} catch { Write-Output ('ERREUR|' + $_.Exception.Message) }
[GC]::Collect(); [GC]::WaitForPendingFinalizers()
Start-Sleep -Seconds 2
& reg.exe unload 'HKU\JVTECH-Ecran' | Out-Null
";
        var ok = false;
        foreach (var line in PowerShell.Run(script, TimeSpan.FromMinutes(2)))
        {
            if (line == "OK") ok = true;
            else if (line.StartsWith("ERREUR|", StringComparison.Ordinal)) log.Report("Mise à l'échelle non réglée : " + line.Substring(7));
        }
        if (ok) log.Report("Mise à l'échelle à 100 % pour le compte de l'acheteur.");
        return ok;
    }

    // ---------- Windows ----------

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DM_BITSPERPEL = 0x40000, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    private const uint CDS_UPDATEREGISTRY = 0x1, CDS_GLOBAL = 0x8;
    private const int DISP_CHANGE_SUCCESSFUL = 0, DISP_CHANGE_RESTART = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);
}
