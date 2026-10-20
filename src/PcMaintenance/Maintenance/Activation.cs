using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
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
            // Le texte de slmgr fait foi quand il est clair (c'est ce que voit le technicien) ;
            // la licence sert pour les autres cas et pour les langues que le texte ne couvre pas.
            return new Activation(PermanentText(text) ? ActivationState.Permanent : FromLicense() ?? FromText(text), text);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new Activation(ActivationState.Unknown, "Impossible de lancer slmgr : " + ex.Message);
        }
    });

    /// <summary>
    /// État lu directement dans la licence Windows (indépendant de la langue) :
    /// LicenseStatus 1 = activé, et sans délai de grâce restant l'activation n'expire pas.
    /// </summary>
    private static ActivationState? FromLicense()
    {
        const string windowsApplication = "55c92734-d682-4d71-983e-d6ec3f16059f";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus, GracePeriodRemaining FROM SoftwareLicensingProduct " +
                $"WHERE ApplicationID = '{windowsApplication}' AND PartialProductKey IS NOT NULL");
            var licenses = searcher.Get().Cast<ManagementBaseObject>().ToList();
            if (licenses.Count == 0) return null;
            var active = licenses.FirstOrDefault(l => Convert.ToUInt32(l["LicenseStatus"]) == 1);
            if (active is null) return ActivationState.NotActivated;
            return Convert.ToUInt32(active["GracePeriodRemaining"]) == 0 ? ActivationState.Permanent : ActivationState.Expires;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>« La machine est activée de manière permanente », « L'ordinateur est définitivement activé », « permanently activated ».</summary>
    private static bool PermanentText(string text) =>
        Regex.IsMatch(text.Normalize(NormalizationForm.FormC), "permanent|d.{1,2}finitivement", RegexOptions.IgnoreCase);

    /// <summary>Repli si la licence est illisible : le texte est traduit selon la langue de Windows.</summary>
    private static ActivationState FromText(string text)
    {
        if (PermanentText(text)) return ActivationState.Permanent;
        // Activation par volume (KMS) ou période d'essai : une date d'expiration est donnée.
        if (Regex.IsMatch(text, @"\d{1,4}[/.\-]\d{1,2}[/.\-]\d{1,4}")) return ActivationState.Expires;
        return ActivationState.NotActivated;
    }
}

internal enum ActivationState { Permanent, Expires, NotActivated, Unknown }
