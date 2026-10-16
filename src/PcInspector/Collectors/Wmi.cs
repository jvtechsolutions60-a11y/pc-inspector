using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace PcInspector.Collectors;

/// <summary>Petites aides pour interroger WMI sans répéter le même code partout.</summary>
internal static class Wmi
{
    public const string StorageScope = @"root\Microsoft\Windows\Storage";
    public const string WmiScope = @"root\WMI";

    public static List<ManagementObject> Query(string wql, string scope = @"root\CIMV2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    /// <summary>
    /// Comme <see cref="Query"/>, mais renvoie une liste vide si la classe n'existe pas sur ce PC
    /// ou si l'accès est refusé (fréquent pour les sondes de température et le SMART).
    /// </summary>
    public static List<ManagementObject> QueryOptional(string wql, string scope = @"root\CIMV2")
    {
        try { return Query(wql, scope); }
        catch (ManagementException) { return new List<ManagementObject>(); }
        catch (COMException) { return new List<ManagementObject>(); }
        catch (UnauthorizedAccessException) { return new List<ManagementObject>(); }
    }

    public static string? Str(this ManagementBaseObject obj, string property)
    {
        try
        {
            var value = obj[property];
            if (value is Array array)
                return string.Join(", ", array.Cast<object>().Select(v => v?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)));
            return value?.ToString();
        }
        catch (ManagementException) { return null; }
    }

    public static ulong? ULong(this ManagementBaseObject obj, string property)
    {
        try { return obj[property] is { } v ? Convert.ToUInt64(v) : null; }
        catch (ManagementException) { return null; }
        catch (FormatException) { return null; }
        catch (InvalidCastException) { return null; }
    }

    public static bool? Bool(this ManagementBaseObject obj, string property)
    {
        try { return obj[property] is bool b ? b : null; }
        catch (ManagementException) { return null; }
    }

    /// <summary>Convertit une date WMI (20230115000000.000000+000) en date lisible.</summary>
    public static string? Date(this ManagementBaseObject obj, string property)
    {
        var raw = obj.Str(property);
        if (string.IsNullOrEmpty(raw)) return null;
        try { return ManagementDateTimeConverter.ToDateTime(raw).ToString("dd/MM/yyyy"); }
        catch (ArgumentOutOfRangeException) { return raw; }
    }

    /// <summary>Les tableaux d'entiers WMI (ex. nom d'un écran) codent du texte caractère par caractère.</summary>
    public static string? CharArray(this ManagementBaseObject obj, string property)
    {
        try
        {
            if (obj[property] is not Array array) return null;
            var chars = array.Cast<object>().Select(Convert.ToInt32).TakeWhile(c => c != 0).Select(c => (char)c).ToArray();
            return new string(chars).Trim();
        }
        catch (ManagementException) { return null; }
    }

    public static string FormatBytes(ulong? bytes)
    {
        if (bytes is null) return "—";
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double value = bytes.Value;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }

    public static string YesNo(bool? value) => value switch
    {
        true => "Oui",
        false => "Non",
        null => "—",
    };
}
