using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace PcMaintenance.Preparation;

/// <summary>
/// Relevé et remise exacte d'une valeur de registre HKLM changée par la préparation : existence, type et valeur.
/// Le relevé est un texte court gardé dans etat.ini (« absent », « DWord:2 », « String:base64 »…).
/// </summary>
internal static class PolicyBackup
{
    public const string Absent = "absent";
    private const string PowerSchemesKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

    public static string Read(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null || key.GetValue(name) is not { } value) return Absent;
            return Encode(key.GetValueKind(name), value);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { return Absent; }
    }

    /// <summary>Remet la valeur relevée. Null si c'est fait, sinon la raison.</summary>
    public static string? Restore(string path, string name, string before)
    {
        try
        {
            if (before == Absent)
            {
                using var key = Registry.LocalMachine.OpenSubKey(path, writable: true);
                key?.DeleteValue(name, throwOnMissingValue: false);
                return null;
            }
            if (Decode(before) is not { } decoded) return "relevé illisible : valeur laissée";
            using (var key = Registry.LocalMachine.CreateSubKey(path)) key?.SetValue(name, decoded.Value, decoded.Kind);
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException)
        {
            return ex.Message;
        }
    }

    internal static string Encode(RegistryValueKind kind, object value) => kind switch
    {
        RegistryValueKind.DWord => "DWord:" + unchecked((uint)(int)value),
        RegistryValueKind.QWord => "QWord:" + unchecked((ulong)(long)value),
        RegistryValueKind.String or RegistryValueKind.ExpandString => kind + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes((string)value)),
        _ => "Autre:" + kind,
    };

    /// <summary>Null si le relevé n'est pas d'un type que l'on sait remettre (on ne touche alors à rien).</summary>
    internal static (RegistryValueKind Kind, object Value)? Decode(string text)
    {
        var parts = text.Split(new[] { ':' }, 2);
        if (parts.Length != 2) return null;
        try
        {
            return parts[0] switch
            {
                "DWord" => (RegistryValueKind.DWord, unchecked((int)uint.Parse(parts[1]))),
                "QWord" => (RegistryValueKind.QWord, unchecked((long)ulong.Parse(parts[1]))),
                "String" => (RegistryValueKind.String, Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))),
                "ExpandString" => (RegistryValueKind.ExpandString, Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return null; }
    }

    /// <summary>GUID du plan d'alimentation actif (registre, lecture seule), null si illisible.</summary>
    public static string? ActivePowerScheme()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PowerSchemesKey);
            return key?.GetValue("ActivePowerScheme") is string guid && Guid.TryParse(guid, out var parsed) ? parsed.ToString() : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { return null; }
    }
}
