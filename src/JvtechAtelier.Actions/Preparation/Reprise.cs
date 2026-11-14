using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Jvtech.Atelier.Actions.Preparation;

/// <summary>
/// Reprise de la préparation après un redémarrage : une entrée RunOnce du compte (Windows l'efface après l'avoir
/// lancée une fois) relance Atelier avec /reprendre. Le redémarrage n'est fait que sur clic du technicien.
/// </summary>
public static class Reprise
{
    public const string Argument = "/reprendre";
    private const string Cle = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Nom = "JVTECH Atelier";

    public static bool Demandee(string[] arguments) =>
        Array.Exists(arguments, a => string.Equals(a.Trim(), Argument, StringComparison.OrdinalIgnoreCase));

    /// <summary>Commande RunOnce : chemin entre guillemets (espaces), puis l'argument de reprise.</summary>
    public static string Commande(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || exe.Contains("\"")) throw new ArgumentException("Chemin du programme invalide.", nameof(exe));
        return $"\"{exe}\" {Argument}";
    }

    public static void Programmer(string exe)
    {
        using var cle = Registry.CurrentUser.CreateSubKey(Cle);
        cle.SetValue(Nom, Commande(exe), RegistryValueKind.String);
    }

    public static void Annuler()
    {
        using var cle = Registry.CurrentUser.OpenSubKey(Cle, writable: true);
        cle?.DeleteValue(Nom, throwOnMissingValue: false);
    }

    public static bool EstProgrammee()
    {
        using var cle = Registry.CurrentUser.OpenSubKey(Cle);
        return cle?.GetValue(Nom) is string;
    }

    /// <summary>Redémarrage dans 30 secondes (annulable avec « shutdown /a »). Renvoie false si la commande échoue.</summary>
    public static bool Redemarrer()
    {
        var shutdown = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(shutdown, "/r /t 30 /c \"JVTECH Atelier : redémarrage pour les mises à jour, reprise à l'ouverture de session\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            p.WaitForExit(15000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
