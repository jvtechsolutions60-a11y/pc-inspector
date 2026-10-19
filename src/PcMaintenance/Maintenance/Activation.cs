using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PcMaintenance.Maintenance;

/// <summary>Résultat de « slmgr /xpr » : l'activation de Windows est-elle permanente ?</summary>
internal sealed record Activation(ActivationState State, string Text)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Lance slmgr.vbs avec cscript pour lire le texte au lieu de la fenêtre que slmgr ouvre d'habitude.
    /// Lecture seule : /xpr affiche seulement la date d'expiration de l'activation.
    /// </summary>
    public static Task<Activation> ReadAsync() => Task.Run(() =>
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var start = new ProcessStartInfo(Path.Combine(system, "cscript.exe"), $"//NoLogo \"{Path.Combine(system, "slmgr.vbs")}\" /xpr")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            // cscript écrit dans la page de code de la console (850 en français) : sans ça, les accents sont abîmés.
            StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
        };
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return new Activation(ActivationState.Unknown, "slmgr n'a pas répondu.");
            }
            var lines = output.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) return new Activation(ActivationState.Unknown, "slmgr n'a rien renvoyé.");
            var text = string.Join("\n", lines);
            return new Activation(Classify(text), text);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new Activation(ActivationState.Unknown, "Impossible de lancer slmgr : " + ex.Message);
        }
    });

    /// <summary>Le texte est traduit selon la langue de Windows : on cherche les mots qui reviennent en français et en anglais.</summary>
    private static ActivationState Classify(string text)
    {
        if (Regex.IsMatch(text, "permanent", RegexOptions.IgnoreCase)) return ActivationState.Permanent;
        // Activation par volume (KMS) ou période d'essai : une date d'expiration est donnée.
        if (Regex.IsMatch(text, @"\d{1,4}[/.\-]\d{1,2}[/.\-]\d{1,4}")) return ActivationState.Expires;
        return ActivationState.NotActivated;
    }
}

internal enum ActivationState { Permanent, Expires, NotActivated, Unknown }
