using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PcInspector;

namespace PcMaintenance.Preparation;

/// <summary>
/// Win11Debloat (github.com/Raphire/Win11Debloat, licence MIT), version figée fournie avec le kit dans C:\JVTECH\Win11Debloat.
/// Lancé en mode Sysprep : les réglages vont dans le profil par défaut, donc dans le compte que l'acheteur créera.
/// Seules des options sans risque pour un PC vendu : rien sur Windows Update, Defender, le Store ou Edge.
/// </summary>
internal static class Win11Debloat
{
    private const string Arguments =
        "-Silent -Sysprep -RemoveApps -DisableTelemetry -DisableBing -DisableSuggestions -DisableWidgets -DisableCopilot -DisableRecall";

    public static string ScriptPath => Path.Combine(AppPaths.ExeFolder, "Win11Debloat", "Win11Debloat.ps1");

    public static bool Available => File.Exists(ScriptPath);

    /// <summary>
    /// Empreinte attendue des scripts de Win11Debloat, fixée à la compilation par la CI (variable WIN11DEBLOAT_SHA256 de
    /// build.yml, la même que celle contrôlée au téléchargement). Vide : compilation locale, Win11Debloat n'est pas lancé.
    /// </summary>
    internal static string ExpectedHash =>
        typeof(Win11Debloat).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "Win11DebloatSha256")?.Value?.Trim().ToLowerInvariant() ?? "";

    private static readonly string[] ScriptExtensions = { ".ps1", ".psm1", ".psd1", ".reg", ".bat", ".cmd", ".vbs", ".exe", ".dll" };

    /// <summary>
    /// Empreinte d'un dossier : pour chaque fichier exécutable ou de réglages (.ps1, .reg…), « chemin/relatif|sha256 »,
    /// triés (ordre ordinal), une ligne par fichier (\n), puis SHA-256 de ce texte en UTF-8. Même calcul que build.yml.
    /// Les journaux et réglages écrits par le script lui-même ne comptent pas.
    /// </summary>
    internal static string FolderHash(string folder)
    {
        var root = Path.GetFullPath(folder).TrimEnd('\\');
        using var sha = SHA256.Create();
        var lines = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => ScriptExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(f =>
            {
                using var stream = File.OpenRead(f);
                return f.Substring(root.Length + 1).Replace('\\', '/') + "|" + Hex(sha.ComputeHash(stream));
            })
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToArray();
        return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
    }

    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

    /// <summary>Renvoie null si tout s'est bien passé, sinon la raison de l'échec.</summary>
    public static string? Run(IProgress<string> log)
    {
        // Audit I09 : jamais de script tiers modifié. Empreinte inconnue ou différente : Win11Debloat n'est pas lancé.
        var expected = ExpectedHash;
        string actual;
        try { actual = FolderHash(Path.GetDirectoryName(ScriptPath)!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "scripts illisibles : non lancé (" + ex.Message + ")"; }
        if (expected.Length == 0) return "empreinte attendue absente (version compilée hors GitHub) : non lancé par sécurité";
        if (actual != expected) return $"scripts différents de la version vérifiée (empreinte {actual.Substring(0, 12)}…) : non lancé par sécurité";
        var logs = Path.Combine(AppPaths.ExeFolder, "Win11Debloat", "Journaux");
        Directory.CreateDirectory(logs);
        log.Report("Win11Debloat " + Arguments);
        var lines = PowerShell.Run(
            $"& {PowerShell.Quote(ScriptPath)} {Arguments} -LogPath {PowerShell.Quote(logs)} *>&1 | ForEach-Object {{ \"$_\" }}\n'FIN|' + $LASTEXITCODE",
            TimeSpan.FromMinutes(30));
        foreach (var line in lines.Where(l => !l.StartsWith("FIN|") && l.Trim('-', '=', ' ').Length > 0))
            log.Report("    " + line);
        var failure = lines.FirstOrDefault(l => l.StartsWith("ERREUR|PowerShell|"));
        if (failure is not null) return failure.Substring("ERREUR|PowerShell|".Length);
        // Un script PowerShell sans « exit » laisse $LASTEXITCODE vide : c'est une réussite.
        return lines.Contains("FIN|") || lines.Contains("FIN|0") ? null : "terminé avec une erreur (voir le journal)";
    }
}
