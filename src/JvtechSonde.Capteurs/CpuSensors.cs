using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace JvtechSonde.Capteurs;

/// <summary>
/// Appelé par JVTECH Sonde (par réflexion, voir CpuTemperatureReader) : température du processeur lue par
/// LibreHardwareMonitor. Seul le processeur est ouvert, aucun réglage n'est modifié.
/// </summary>
public static class CpuSensors
{
    // Les capteurs qui représentent le mieux la puce entière, du plus fiable au moins fiable.
    private static readonly string[] Preferred = { "Package", "Tctl/Tdie", "Tdie", "Tctl", "Core Max", "Core Average" };

    /// <summary>Température du processeur en °C, ou null si aucun capteur n'est lisible.</summary>
    public static double? CpuTemperature()
    {
        var computer = new Computer { IsCpuEnabled = true };
        try
        {
            computer.Open();
            var temperatures = new List<(string Name, float Value)>();
            foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
            {
                hardware.Update();
                foreach (var sub in hardware.SubHardware) sub.Update();
                foreach (var sensor in hardware.Sensors.Concat(hardware.SubHardware.SelectMany(s => s.Sensors)))
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value is { } v && v > 0)
                        temperatures.Add((sensor.Name, v));
                }
            }
            if (temperatures.Count == 0) return null;
            foreach (var name in Preferred)
            {
                var match = temperatures.Where(t => t.Name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (match.Count > 0) return match.Max(t => t.Value);
            }
            return temperatures.Max(t => t.Value);
        }
        finally
        {
            computer.Close();
        }
    }
}
