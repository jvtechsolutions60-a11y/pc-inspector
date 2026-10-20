using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PcInspector.Report;

/// <summary>
/// Transforme le rapport HTML en PDF avec Microsoft Edge (présent sur Windows 10 et 11) lancé en arrière-plan.
/// Rien n'est installé ni modifié : Edge imprime la page dans un fichier, puis se ferme.
/// </summary>
public static class PdfPrinter
{
    public static void FromHtml(string html, string pdfPath)
    {
        var browser = FindBrowser() ?? throw new IOException("Microsoft Edge ou Google Chrome est introuvable : exporter en HTML puis imprimer en PDF depuis le navigateur.");

        var work = AppPaths.EnsureFolder(Path.Combine(AppPaths.WorkFolder, "pdf-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
        var htmlPath = Path.Combine(work, "rapport.html");
        File.WriteAllText(htmlPath, html, new System.Text.UTF8Encoding(true));
        if (File.Exists(pdfPath)) File.Delete(pdfPath);

        try
        {
            // Profil à part : sinon Edge confie la page à une fenêtre déjà ouverte et ne produit pas le PDF.
            var args = $"--headless --disable-gpu --no-first-run --no-pdf-header-footer --print-to-pdf-no-header " +
                       $"--user-data-dir=\"{Path.Combine(work, "profil")}\" --print-to-pdf=\"{pdfPath}\" \"{new Uri(htmlPath).AbsoluteUri}\"";
            using var process = Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false, CreateNoWindow = true })
                                ?? throw new IOException("Impossible de lancer " + Path.GetFileName(browser) + ".");
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                throw new IOException("La création du PDF a pris trop de temps.");
            }
            if (!File.Exists(pdfPath)) throw new IOException("Le navigateur n'a pas produit le PDF : exporter en HTML puis imprimer en PDF.");
        }
        finally
        {
            try { Directory.Delete(work, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string? FindBrowser()
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
        return candidates.FirstOrDefault(File.Exists);
    }
}
