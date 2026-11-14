using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PcInspector.Report;

/// <summary>
/// Transforme le rapport HTML en PDF avec Microsoft Edge (présent sur Windows 10 et 11) lancé en arrière-plan.
/// Rien n'est installé ni modifié : Edge imprime la page dans un fichier, puis se ferme.
/// </summary>
public static class PdfPrinter
{
    public static void FromHtml(string html, string pdfPath)
    {
        var browsers = FindBrowsers();
        if (browsers.Count == 0) throw new IOException("Microsoft Edge ou Google Chrome est introuvable : exporter en HTML puis imprimer en PDF depuis le navigateur.");

        var work = AppPaths.EnsureFolder(Path.Combine(AppPaths.WorkFolder, "pdf-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        var htmlPath = Path.Combine(work, "rapport.html");
        // Le navigateur écrit d'abord dans le dossier de travail (chemin simple, hors OneDrive), puis le PDF est déplacé.
        var tempPdf = Path.Combine(work, "rapport.pdf");
        File.WriteAllText(htmlPath, html, new System.Text.UTF8Encoding(true));

        var errors = new List<string>();
        try
        {
            // Les versions récentes n'acceptent plus l'ancien mode sans fenêtre : on essaie le nouveau puis l'ancien.
            foreach (var browser in browsers)
            {
                foreach (var headless in new[] { "--headless=new", "--headless" })
                {
                    var error = TryPrint(browser, headless, htmlPath, tempPdf, Path.Combine(work, "profil-" + errors.Count));
                    if (error is null)
                    {
                        if (File.Exists(pdfPath)) File.Delete(pdfPath);
                        File.Move(tempPdf, pdfPath);
                        return;
                    }
                    errors.Add($"{Path.GetFileNameWithoutExtension(browser)} {headless} : {error}");
                }
            }
            throw new IOException("Le navigateur n'a pas produit le PDF : exporter en HTML puis imprimer en PDF.\n\n" + string.Join("\n", errors));
        }
        finally
        {
            try { Directory.Delete(work, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Renvoie null si le PDF a été créé, sinon ce qui s'est passé.</summary>
    private static string? TryPrint(string browser, string headless, string htmlPath, string pdfPath, string profile)
    {
        if (File.Exists(pdfPath)) File.Delete(pdfPath);
        // Profil à part : sinon Edge confie la page à une fenêtre déjà ouverte et ne produit pas le PDF.
        // --no-sandbox : le bac à sable refuse parfois de démarrer quand le logiciel est lancé en administrateur.
        var args = $"{headless} --disable-gpu --no-sandbox --no-first-run --no-default-browser-check --disable-extensions " +
                   $"--no-pdf-header-footer --print-to-pdf-no-header --user-data-dir=\"{profile}\" " +
                   $"--print-to-pdf=\"{pdfPath}\" \"{new Uri(htmlPath).AbsoluteUri}\"";
        var output = new List<string>();
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(browser, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                },
            };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lock (output) output.Add(e.Data!.Trim()); };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) lock (output) output.Add(e.Data!.Trim()); };
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return "trop long";
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return "lancement impossible (" + ex.Message + ")";
        }

        // Le navigateur peut finir d'écrire le fichier juste après la fin du processus principal.
        for (var i = 0; i < 20; i++)
        {
            try
            {
                if (File.Exists(pdfPath) && new FileInfo(pdfPath).Length > 0) return null;
            }
            catch (IOException) { }
            Thread.Sleep(500);
        }
        lock (output) return output.Count == 0 ? "aucun fichier" : "aucun fichier · " + string.Join(" ", output.Skip(Math.Max(0, output.Count - 2)));
    }

    private static List<string> FindBrowsers()
    {
        var roots = new[]
        {
            Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            Environment.GetEnvironmentVariable("ProgramFiles"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        }.Where(r => !string.IsNullOrEmpty(r)).ToList();
        var candidates = new List<string>();
        foreach (var root in roots) candidates.Add(Path.Combine(root!, @"Microsoft\Edge\Application\msedge.exe"));
        foreach (var root in roots) candidates.Add(Path.Combine(root!, @"Google\Chrome\Application\chrome.exe"));
        return candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
