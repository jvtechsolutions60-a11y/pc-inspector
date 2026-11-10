using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PcInspector;

namespace PcMigration.Migration;

/// <summary>Ce dont une sauvegarde ou une restauration a besoin, et le lien avec la fenêtre.</summary>
internal sealed class JobContext
{
    public JobContext(Layout layout, UserProfile profile, string? password, IProgress<string> log, Func<string, bool> ask, Func<bool> stop)
    {
        Layout = layout; Profile = profile; Password = password; Log = log; Ask = ask; Stop = stop;
    }

    public Layout Layout { get; }
    public UserProfile Profile { get; }
    public string? Password { get; }
    public IProgress<string> Log { get; }
    /// <summary>Message OK / Annuler affiché par la fenêtre : true = OK.</summary>
    public Func<string, bool> Ask { get; }
    public Func<bool> Stop { get; }
    public MigrationReport Report { get; } = new();
    /// <summary>Lignes à remettre dans l'état de la fenêtre : élément → résumé.</summary>
    public Dictionary<string, string> Results { get; } = new();
    /// <summary>Éléments incomplets ou non faits : la fin de la sauvegarde ne peut pas être annoncée comme réussie.</summary>
    public List<string> Problems { get; } = new();

    /// <summary>Note une copie incomplète (fichiers manquants listés dans le rapport).</summary>
    public void Check(string title, CopyResult result)
    {
        if (result.IsComplete) return;
        lock (Problems) Problems.Add($"{title} : {result.Summary()}");
        if (result.FailedFiles.Count > 0) Report.Detail($"Fichiers non copiés : {title}", result.FailedFiles);
    }

    /// <summary>Lance une étape : une erreur imprévue la note « non faite » et on passe à la suivante.</summary>
    public void Run(string key, string title, Func<string> step)
    {
        if (Stop()) return;
        Log.Report($"--- {title} ---");
        string result;
        try { result = step(); }
        catch (Exception ex)
        {
            CrashGuard.Write(ex, fatal: false);
            result = $"Non fait : erreur imprévue ({ex.Message})";
        }
        Log.Report($"{title} : {result}");
        if (result.StartsWith("Non fait", StringComparison.Ordinal)) lock (Problems) Problems.Add($"{title} : {result}");
        Report.Step(title, result);
        Results[key] = result;
    }

    /// <summary>Attend qu'un logiciel soit fermé. False si le technicien préfère passer.</summary>
    public bool WaitClosed(string process, string name)
    {
        while (Tools.IsRunning(process) && !Tools.StopBackground(process))
            if (!Ask($"{name} est ouvert. Le fermer (toutes ses fenêtres), puis cliquer OK.\n\nAnnuler : passer {name}.")) return false;
        return true;
    }
}

/// <summary>Sauvegarde de l'ancien PC vers le disque externe.</summary>
internal static class Backup
{
    public static void Run(JobContext ctx, ICollection<string> items)
    {
        var l = ctx.Layout;
        // D'abord ce qui demande le technicien (mots de passe, navigateurs à fermer), puis le long (dossiers) : il peut partir.
        if (items.Contains(Items.Passwords)) ctx.Run(Items.Passwords, "Mots de passe des navigateurs", () => Passwords(ctx));
        if (items.Contains(Items.Apps)) ctx.Run(Items.Apps, "Applications", () => Apps(ctx));
        if (items.Contains(Items.WiFi)) ctx.Run(Items.WiFi, "Réseaux Wi-Fi", () => WiFi(ctx));
        if (items.Contains(Items.Browsers)) ctx.Run(Items.Browsers, "Navigateurs (favoris, historique, extensions, onglets)", () => BrowserProfiles(ctx));
        if (items.Contains(Items.Outlook)) ctx.Run(Items.Outlook, "Outlook (fichiers .pst et signatures)", () => Outlook(ctx, items));
        if (items.Contains(Items.Wallpaper)) ctx.Run(Items.Wallpaper, "Fond d'écran", () => Wallpaper(ctx));
        if (items.Contains(Items.Printers)) ctx.Run(Items.Printers, "Imprimantes", () => Printers(ctx));
        foreach (var folder in ctx.Profile.Folders().Where(f => items.Contains(Items.FolderPrefix + f.Key)))
            ctx.Run(Items.FolderPrefix + folder.Key, folder.Label, () => Folder(ctx, folder));
    }

    // ---------- Dossiers ----------

    private static string Folder(JobContext ctx, UserFolder folder)
    {
        if (!Directory.Exists(folder.Path)) return "Dossier absent";
        if (folder.Path.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
            ctx.Log.Report("    Dossier synchronisé avec OneDrive : les fichiers « en ligne seulement » sont téléchargés pendant la copie.");
        ctx.Log.Report($"    {folder.Path} → {Path.Combine(ctx.Layout.Folders, folder.Key)}");
        var result = Tools.Robocopy(folder.Path, Path.Combine(ctx.Layout.Folders, folder.Key), ctx.Log);
        ctx.Check(folder.Label, result);
        return result.Summary();
    }

    // ---------- Navigateurs ----------

    private static string BrowserProfiles(JobContext ctx)
    {
        var done = new List<string>();
        foreach (var browser in Browser.InstalledFor(ctx.Profile))
        {
            if (ctx.Stop()) break;
            if (!ctx.WaitClosed(browser.Process, browser.Name)) { done.Add($"{browser.Name} : passé (resté ouvert)"); continue; }
            ctx.Log.Report($"    {browser.Name}…");
            var result = Tools.Robocopy(browser.PathIn(ctx.Profile), Path.Combine(ctx.Layout.Browsers, browser.Key), ctx.Log,
                excludeDirs: Browser.CacheFolders);
            ctx.Check("Navigateur " + browser.Name, result);
            done.Add($"{browser.Name} : {result.Summary()}");
        }
        ctx.Report.Detail("Navigateurs sauvegardés", done);
        if (done.Count == 0) return "Aucun navigateur trouvé";
        return string.Join(" ; ", done.Select(d => d.Split(':')[0].Trim()));
    }

    /// <summary>
    /// Chrome, Edge, Brave… chiffrent les mots de passe avec le compte Windows : copiés tels quels, ils sont illisibles
    /// sur le nouveau PC. On passe donc par l'export CSV du navigateur, chiffré aussitôt avec le mot de passe de la sauvegarde.
    /// </summary>
    private static string Passwords(JobContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Password)) return "Non fait : mot de passe de la sauvegarde manquant";
        Directory.CreateDirectory(ctx.Layout.Passwords);
        var done = new List<string>();
        // Le navigateur écrit le CSV en clair : on le fait enregistrer sur le disque du PC, dans un dossier local
        // réservé (jamais sur le disque externe), et on le vide quoi qu'il arrive.
        var secrets = Secrets.Create(ctx.Profile);
        try
        {
            foreach (var browser in Browser.InstalledFor(ctx.Profile).Where(b => b.Chromium))
            {
                if (ctx.Stop()) break;
                if (!browser.MayHavePasswords(ctx.Profile)) { ctx.Log.Report($"    {browser.Name} : aucun mot de passe enregistré."); continue; }
                var since = DateTime.Now.AddSeconds(-5);
                if (!browser.OpenPasswordsPage()) ctx.Log.Report($"    {browser.Name} n'a pas pu être ouvert : l'ouvrir à la main.");
                var asked = ctx.Ask(
                    $"{browser.Name} : exporter les mots de passe.\n\n" +
                    $"1. Dans {browser.Name}, ouvert sur la page des mots de passe ({browser.PasswordsPage}), cliquer « Exporter les mots de passe » (ou « Télécharger le fichier »).\n" +
                    $"2. Enregistrer le fichier dans ce dossier du PC (pas sur le disque externe) :\n{secrets}\n(Téléchargements ou Documents marchent aussi, il sera retrouvé).\n\n" +
                    "Cliquer OK une fois le fichier enregistré. Annuler : passer ce navigateur.\n\n" +
                    "Le fichier est chiffré tout de suite avec le mot de passe de la sauvegarde, puis effacé.");
                if (!asked) { done.Add($"{browser.Name} : passé"); continue; }
                var csv = FindExport(ctx, since, secrets);
                if (csv is null) { done.Add($"{browser.Name} : fichier CSV introuvable, non sauvegardé"); continue; }
                try
                {
                    var bytes = File.ReadAllBytes(csv);
                    var count = Math.Max(0, Encoding.UTF8.GetString(bytes).Split('\n').Count(line => line.Trim().Length > 0) - 1);
                    var target = Path.Combine(ctx.Layout.Passwords, browser.Key + ".csv");
                    Vault.Encrypt(bytes, target + Vault.Extension, ctx.Password!);
                    Array.Clear(bytes, 0, bytes.Length);
                    done.Add($"{browser.Name} : {count} mots de passe (chiffrés)");
                }
                finally
                {
                    // Effacé même si le chiffrement échoue (disque plein, débranché…) : jamais de CSV laissé en clair.
                    Tools.Wipe(csv);
                }
            }
        }
        finally
        {
            Secrets.Cleanup(ctx.Profile);
        }
        if (Browser.InstalledFor(ctx.Profile).Any(b => b.Key == "Firefox"))
            done.Add("Mozilla Firefox : les mots de passe suivent le profil Firefox (élément Navigateurs)");
        ctx.Report.Detail("Mots de passe", done);
        return done.Count == 0 ? "Aucun mot de passe enregistré dans les navigateurs" : string.Join(" ; ", done);
    }

    private static string? FindExport(JobContext ctx, DateTime since, string secrets)
    {
        var places = new[] { secrets, ctx.Layout.Passwords, ctx.Profile.FolderByKey("Documents")?.Path, ctx.Profile.FolderByKey("Downloads")?.Path,
                             ctx.Profile.FolderByKey("Desktop")?.Path };
        var files = new List<FileInfo>();
        foreach (var place in places.Where(p => p is not null && Directory.Exists(p)))
        {
            try { files.AddRange(new DirectoryInfo(place!).GetFiles("*.csv").Where(f => f.LastWriteTime >= since)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return files.OrderByDescending(f => f.LastWriteTime).FirstOrDefault()?.FullName;
    }

    // ---------- Applications ----------

    private static string Apps(JobContext ctx)
    {
        Directory.CreateDirectory(ctx.Layout.Apps);
        var installed = InstalledPrograms(ctx.Profile);
        File.WriteAllLines(Path.Combine(ctx.Layout.Apps, "Programmes installés.txt"), installed, Encoding.UTF8);

        var output = new List<string>();
        var code = Winget.Run($"export -o {Tools.Quote(ctx.Layout.WingetFile)} --accept-source-agreements --disable-interactivity", output, TimeSpan.FromMinutes(10));
        if (code is null || !File.Exists(ctx.Layout.WingetFile))
        {
            foreach (var line in output.Take(10)) ctx.Log.Report("      " + line);
            // Sans winget, tout est à réinstaller à la main : la liste complète va dans le rapport et dans le fichier lu à la restauration.
            File.WriteAllLines(Path.Combine(ctx.Layout.Apps, "Sans winget (à réinstaller à la main).txt"), installed, Encoding.UTF8);
            ctx.Report.Detail("Applications à réinstaller à la main (winget indisponible)", installed);
            return $"Liste des programmes enregistrée ({installed.Count}), mais winget n'a pas pu exporter : réinstallation à la main";
        }
        var ids = Winget.ReadIds(ctx.Layout.WingetFile);
        // « Installed package is not available from any source: X » (le texte change avec la langue de winget).
        var manual = output.Select(l => l.Trim())
            .Select(l => Regex.Match(l, @"(?i)(not available|aucune source|n'est pas disponible|introuvable).*?:\s*(.+)$"))
            .Where(m => m.Success).Select(m => m.Groups[2].Value.Trim()).Distinct().OrderBy(n => n).ToList();
        File.WriteAllLines(Path.Combine(ctx.Layout.Apps, "Sans winget (à réinstaller à la main).txt"), manual, Encoding.UTF8);
        ctx.Report.Detail("Applications réinstallables automatiquement (winget)", ids);
        ctx.Report.Detail("Applications à réinstaller à la main (pas dans winget)", manual);
        return $"{ids.Count} réinstallables automatiquement, {manual.Count} à réinstaller à la main";
    }

    /// <summary>Programmes de « Applications et fonctionnalités », pour garder une trace même sans winget.</summary>
    private static List<string> InstalledPrograms(UserProfile profile)
    {
        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        const string uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var roots = new List<RegistryKey?>();
        try
        {
            roots.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(uninstall));
            roots.Add(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(uninstall));
            roots.Add(Registry.Users.OpenSubKey(profile.Sid + "\\" + uninstall));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        foreach (var root in roots.Where(r => r is not null))
        {
            using (root)
            {
                foreach (var sub in root!.GetSubKeyNames())
                {
                    try
                    {
                        using var key = root.OpenSubKey(sub);
                        if (key?.GetValue("DisplayName") is not string name || name.Trim().Length == 0) continue;
                        if (key.GetValue("SystemComponent") is int system && system == 1) continue;
                        if (key.GetValue("ParentKeyName") is string) continue;
                        names.Add(name.Trim());
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
                }
            }
        }
        return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ---------- Wi-Fi ----------

    private static string WiFi(JobContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Password)) return "Non fait : mot de passe de la sauvegarde manquant (les clés Wi-Fi sont en clair)";
        Directory.CreateDirectory(ctx.Layout.WiFi);
        var names = new List<string>();

        // 1. API Windows : profils et clés lus en mémoire puis chiffrés directement sur le disque externe.
        var profiles = WlanApi.ReadProfiles();
        if (profiles is not null)
        {
            var index = 0;
            foreach (var (name, xml) in profiles)
            {
                var file = Path.Combine(ctx.Layout.WiFi, $"{++index:000} {Tools.SafeName(name)}.xml{Vault.Extension}");
                Vault.Encrypt(Encoding.UTF8.GetBytes(xml), file, ctx.Password!);
                names.Add(name);
            }
            ctx.Report.Detail("Réseaux Wi-Fi enregistrés", names);
            return names.Count == 0 ? "Aucun réseau Wi-Fi enregistré" : $"{names.Count} réseaux (clés chiffrées)";
        }

        // 2. Secours (API indisponible) : export netsh dans le dossier local réservé, jamais sur le disque externe.
        var secrets = Secrets.Create(ctx.Profile);
        try
        {
            var output = new List<string>();
            Tools.Run("netsh.exe", $"wlan export profile key=clear folder={Tools.Quote(secrets)}", output, TimeSpan.FromMinutes(2));
            var index = 0;
            foreach (var file in Directory.GetFiles(secrets, "*.xml"))
            {
                try
                {
                    var name = WiFiName(file);
                    Vault.Encrypt(File.ReadAllBytes(file), Path.Combine(ctx.Layout.WiFi, $"{++index:000} {Tools.SafeName(name)}.xml{Vault.Extension}"), ctx.Password!);
                    names.Add(name);
                }
                finally { Tools.Wipe(file); }
            }
        }
        finally { Secrets.Cleanup(ctx.Profile); }
        ctx.Report.Detail("Réseaux Wi-Fi enregistrés", names);
        return names.Count == 0 ? "Aucun réseau Wi-Fi enregistré (ou pas de carte Wi-Fi)" : $"{names.Count} réseaux (clés chiffrées)";
    }

    internal static string WiFiName(string xmlFile)
    {
        try
        {
            if (WiFiNameFromXml(File.ReadAllText(xmlFile)) is { } name) return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Path.GetFileNameWithoutExtension(xmlFile);
    }

    /// <summary>Nom du réseau dans un profil Wi-Fi (XML), sans rien écrire sur le disque.</summary>
    internal static string? WiFiNameFromXml(string xml)
    {
        var match = Regex.Match(xml, "<name>([^<]+)</name>");
        return match.Success ? System.Net.WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    // ---------- Outlook ----------

    private static string Outlook(JobContext ctx, ICollection<string> items)
    {
        if (!ctx.WaitClosed("OUTLOOK", "Outlook")) return "Passé : Outlook resté ouvert (ses fichiers .pst sont verrouillés)";
        var parts = new List<string>();
        var signatures = Path.Combine(ctx.Profile.Roaming, @"Microsoft\Signatures");
        if (Directory.Exists(signatures))
        {
            var result = Tools.Robocopy(signatures, Path.Combine(ctx.Layout.Outlook, "Signatures"), ctx.Log);
            parts.Add($"signatures : {result.Copied.Files} fichiers");
        }

        // Les .pst de Documents partent avec Documents ; ceux d'AppData (anciens Outlook) seraient perdus sans cette étape.
        var places = new List<string> { Path.Combine(ctx.Profile.Local, @"Microsoft\Outlook") };
        if (!items.Contains(Items.FolderPrefix + "Documents") && ctx.Profile.FolderByKey("Documents") is { } docs)
        {
            places.Add(Path.Combine(docs.Path, "Fichiers Outlook"));
            places.Add(Path.Combine(docs.Path, "Outlook Files"));
        }
        var pst = new List<string>();
        foreach (var place in places.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(place, "*.pst"))
            {
                if (ctx.Stop()) break;
                var target = Path.Combine(ctx.Layout.Outlook, "PST", Path.GetFileName(file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ctx.Log.Report($"    {Path.GetFileName(file)} ({Tools.Size((ulong)new FileInfo(file).Length)})…");
                File.Copy(file, target, overwrite: true);
                pst.Add($"{Path.GetFileName(file)} ({Tools.Size((ulong)new FileInfo(file).Length)})");
            }
        }
        if (pst.Count > 0) parts.Add($"{pst.Count} fichiers .pst");
        ctx.Report.Detail("Fichiers Outlook (.pst)", pst);
        return parts.Count == 0 ? "Rien à sauvegarder (pas de signature ni de .pst hors Documents)" : string.Join(", ", parts);
    }

    // ---------- Fond d'écran et imprimantes ----------

    private static string Wallpaper(JobContext ctx)
    {
        string? source = null;
        try
        {
            using var key = Registry.Users.OpenSubKey(ctx.Profile.Sid + @"\Control Panel\Desktop");
            if (key?.GetValue("WallPaper") is string path && File.Exists(path)) source = path;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        var transcoded = Path.Combine(ctx.Profile.Roaming, @"Microsoft\Windows\Themes\TranscodedWallpaper");
        if (source is null && File.Exists(transcoded)) source = transcoded;
        if (source is null) return "Pas d'image (couleur unie ou diaporama)";
        Directory.CreateDirectory(ctx.Layout.Wallpaper);
        var extension = Path.GetExtension(source);
        File.Copy(source, Path.Combine(ctx.Layout.Wallpaper, "fond" + (extension.Length > 0 ? extension : ".jpg")), overwrite: true);
        return "Image sauvegardée";
    }

    private static string Printers(JobContext ctx)
    {
        var lines = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverName, PortName, Network, Default FROM Win32_Printer");
            foreach (ManagementObject printer in searcher.Get())
            {
                var name = printer["Name"] as string ?? "";
                if (Regex.IsMatch(name, "(?i)PDF|XPS|OneNote|Fax")) continue;
                var where = printer["Network"] is true ? "réseau" : "port " + printer["PortName"];
                lines.Add($"{name} — pilote {printer["DriverName"]}, {where}{(printer["Default"] is true ? " (par défaut)" : "")}");
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
        File.WriteAllLines(ctx.Layout.PrintersFile, lines, Encoding.UTF8);
        ctx.Report.Detail("Imprimantes à réinstaller (pilote du fabricant)", lines);
        return lines.Count == 0 ? "Aucune imprimante" : $"{lines.Count} imprimantes listées (à réinstaller avec le pilote du fabricant)";
    }
}
