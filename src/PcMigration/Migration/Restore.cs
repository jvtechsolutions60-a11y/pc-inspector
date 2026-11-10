using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PcMigration.Migration;

/// <summary>Restauration sur le nouveau PC, dans le compte du client. Rien de ce qui existe déjà n'est supprimé.</summary>
internal static class Restore
{
    /// <summary>Ce que contient une sauvegarde, pour cocher seulement ce qui peut être restauré.</summary>
    public static List<string> Available(Layout l)
    {
        var items = new List<string>();
        if (File.Exists(l.WingetFile)) items.Add(Items.Apps);
        if (Directory.Exists(l.Folders))
            items.AddRange(Directory.GetDirectories(l.Folders).Select(d => Items.FolderPrefix + Path.GetFileName(d)));
        if (Directory.Exists(l.Browsers) && Directory.GetDirectories(l.Browsers).Length > 0) items.Add(Items.Browsers);
        if (Directory.Exists(l.Passwords) && Directory.GetFiles(l.Passwords, "*" + Vault.Extension).Length > 0) items.Add(Items.Passwords);
        if (Directory.Exists(l.WiFi) && Directory.GetFiles(l.WiFi, "*" + Vault.Extension).Length > 0) items.Add(Items.WiFi);
        if (Directory.Exists(l.Outlook)) items.Add(Items.Outlook);
        if (Directory.Exists(l.Wallpaper) && Directory.GetFiles(l.Wallpaper).Length > 0) items.Add(Items.Wallpaper);
        if (File.Exists(l.PrintersFile)) items.Add(Items.Printers);
        return items;
    }

    public static void Run(JobContext ctx, ICollection<string> items, ICollection<string> apps)
    {
        // Les applications d'abord : les navigateurs installés retrouvent ensuite leur profil restauré.
        if (items.Contains(Items.Apps)) ctx.Run(Items.Apps, "Applications", () => Apps(ctx, apps));
        foreach (var key in items.Where(i => i.StartsWith(Items.FolderPrefix)).Select(i => i.Substring(Items.FolderPrefix.Length)))
        {
            var folder = ctx.Profile.FolderByKey(key);
            ctx.Run(Items.FolderPrefix + key, folder?.Label ?? key, () => Folder(ctx, key, folder));
        }
        if (items.Contains(Items.Browsers)) ctx.Run(Items.Browsers, "Navigateurs (favoris, historique, extensions, onglets)", () => BrowserProfiles(ctx));
        if (items.Contains(Items.Passwords)) ctx.Run(Items.Passwords, "Mots de passe des navigateurs", () => Passwords(ctx));
        if (items.Contains(Items.WiFi)) ctx.Run(Items.WiFi, "Réseaux Wi-Fi", () => WiFi(ctx));
        if (items.Contains(Items.Outlook)) ctx.Run(Items.Outlook, "Outlook (fichiers .pst et signatures)", () => Outlook(ctx));
        if (items.Contains(Items.Wallpaper)) ctx.Run(Items.Wallpaper, "Fond d'écran", () => Wallpaper(ctx));
        if (items.Contains(Items.Printers)) ctx.Run(Items.Printers, "Imprimantes", () => Printers(ctx));
    }

    // ---------- Applications ----------

    private static string Apps(JobContext ctx, ICollection<string> ids)
    {
        if (ids.Count == 0) return "Aucune application cochée";
        var done = new List<string>();
        int ok = 0, already = 0, failed = 0, index = 0;
        foreach (var id in ids)
        {
            if (ctx.Stop()) break;
            ctx.Log.Report($"    [{++index}/{ids.Count}] {id}…");
            var result = Winget.Install(id, ctx.Log);
            ctx.Log.Report($"    {id} : {result}");
            done.Add($"{id} : {result}");
            if (result == "installée") ok++;
            else if (result == "déjà installée") already++;
            else failed++;
        }
        ctx.Report.Detail("Applications réinstallées", done);
        var manual = Path.Combine(ctx.Layout.Apps, "Sans winget (à réinstaller à la main).txt");
        if (File.Exists(manual)) ctx.Report.Detail("Applications à réinstaller à la main", File.ReadAllLines(manual));
        return $"{ok} installées, {already} déjà présentes, {failed} en échec";
    }

    // ---------- Dossiers ----------

    private static string Folder(JobContext ctx, string key, UserFolder? folder)
    {
        if (folder is null) return "Non fait : dossier inconnu sur ce PC";
        var source = Path.Combine(ctx.Layout.Folders, key);
        ctx.Log.Report($"    {source} → {folder.Path}");
        // Un fichier plus récent déjà présent sur le nouveau PC est gardé ; rien n'est supprimé.
        var result = Tools.Robocopy(source, folder.Path, ctx.Log, keepNewer: true);
        var restored = result.Source;
        return $"{restored.Files} fichiers ({Tools.Size(restored.Bytes)}) remis dans {folder.Path}" + (result.HasFailures ? ", des erreurs (voir le journal)" : "");
    }

    // ---------- Navigateurs ----------

    private static string BrowserProfiles(JobContext ctx)
    {
        var done = new List<string>();
        foreach (var dir in Directory.GetDirectories(ctx.Layout.Browsers))
        {
            if (ctx.Stop()) break;
            var browser = Browser.ByKey(Path.GetFileName(dir));
            if (browser is null) continue;
            if (!ctx.WaitClosed(browser.Process, browser.Name)) { done.Add($"{browser.Name} : passé (resté ouvert)"); continue; }
            var target = browser.PathIn(ctx.Profile);
            // Le profil créé par le premier lancement sur le nouveau PC est mis de côté, pas supprimé.
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                var aside = $"{target} (avant migration {DateTime.Now:yyyy-MM-dd HH-mm})";
                try { Directory.Move(target, aside); ctx.Log.Report($"    Profil {browser.Name} existant mis de côté : {aside}"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ctx.Log.Report($"    Profil {browser.Name} existant gardé (fusion) : {ex.Message}");
                }
            }
            var result = Tools.Robocopy(dir, target, ctx.Log);
            var note = browser.Chromium ? " ; se reconnecter aux sites, mots de passe à l'étape suivante" : "";
            done.Add($"{browser.Name} : {result.Copied.Files} fichiers{note}");
        }
        ctx.Report.Detail("Navigateurs restaurés", done);
        return done.Count == 0 ? "Aucun navigateur dans la sauvegarde" : string.Join(" ; ", done.Select(d => d.Split(':')[0].Trim()));
    }

    private static string Passwords(JobContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Password)) return "Non fait : mot de passe de la sauvegarde manquant";
        var done = new List<string>();
        foreach (var file in Directory.GetFiles(ctx.Layout.Passwords, "*.csv" + Vault.Extension))
        {
            if (ctx.Stop()) break;
            var key = Path.GetFileName(file).Split('.')[0];
            var browser = Browser.ByKey(key) ?? Browser.ByKey("Edge")!;
            var plain = Vault.Decrypt(file, ctx.Password!);
            if (plain is null) { done.Add($"{browser.Name} : mot de passe de la sauvegarde incorrect"); continue; }
            // Le navigateur n'importe qu'un fichier : il est posé dans le dossier local réservé (pas le Bureau, qui peut
            // être synchronisé avec OneDrive), puis effacé dès l'import fini, même en cas d'erreur.
            var csv = Path.Combine(Secrets.Create(ctx.Profile), $"Mots de passe {browser.Name} - à importer puis effacé.csv");
            File.WriteAllBytes(csv, plain);
            Array.Clear(plain, 0, plain.Length);
            try
            {
                var target = browser;
                if (!target.OpenPasswordsPage() && Browser.ByKey("Edge") is { } edge && edge.OpenPasswordsPage())
                {
                    ctx.Log.Report($"    {browser.Name} n'est pas installé : import dans Microsoft Edge.");
                    target = edge;
                }
                var imported = ctx.Ask(
                    $"{target.Name} : importer les mots de passe.\n\n" +
                    $"1. Dans {target.Name}, ouvert sur la page des mots de passe ({target.PasswordsPage}), cliquer « Importer des mots de passe » (menu « … » dans Edge).\n" +
                    $"2. Choisir le fichier :\n{csv}\n\n" +
                    "Cliquer OK une fois l'import terminé : le fichier est alors effacé. Annuler : passer (le fichier est effacé aussi).");
                done.Add($"{target.Name} : {(imported ? "importés" : "passé")}");
            }
            finally
            {
                Tools.Wipe(csv);
                Secrets.Cleanup(ctx.Profile);
            }
        }
        ctx.Report.Detail("Mots de passe", done);
        return done.Count == 0 ? "Aucun fichier de mots de passe" : string.Join(" ; ", done);
    }

    // ---------- Wi-Fi ----------

    private static string WiFi(JobContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.Password)) return "Non fait : mot de passe de la sauvegarde manquant";
        var names = new List<string>();
        int ok = 0, failed = 0;
        try
        {
            foreach (var file in Directory.GetFiles(ctx.Layout.WiFi, "*.xml" + Vault.Extension))
            {
                var plain = Vault.Decrypt(file, ctx.Password!);
                if (plain is null) { failed++; continue; }
                var text = System.Text.Encoding.UTF8.GetString(plain);
                Array.Clear(plain, 0, plain.Length);
                var name = Backup.WiFiNameFromXml(text) ?? Path.GetFileName(file);

                // 1. API Windows, en mémoire. 2. Secours netsh avec un fichier dans le dossier local réservé, effacé aussitôt.
                var error = WlanApi.AddProfile(text);
                if (error is not null && error != "aucune carte Wi-Fi")
                {
                    var xml = Path.Combine(Secrets.Create(ctx.Profile), Guid.NewGuid().ToString("N") + ".xml");
                    try
                    {
                        File.WriteAllText(xml, text);
                        var output = new List<string>();
                        error = Tools.Run("netsh.exe", $"wlan add profile filename={Tools.Quote(xml)} user=all", output, TimeSpan.FromMinutes(1)) == 0
                            ? null : string.Join(" ", output).Trim();
                    }
                    finally { Tools.Wipe(xml); }
                }
                if (error is null) { ok++; names.Add(name); }
                else { failed++; names.Add($"{name} : non ajouté ({error})"); }
            }
        }
        finally { Secrets.Cleanup(ctx.Profile); }
        ctx.Report.Detail("Réseaux Wi-Fi ajoutés", names);
        return $"{ok} réseaux ajoutés" + (failed > 0 ? $", {failed} en échec" : "");
    }

    // ---------- Outlook ----------

    private static string Outlook(JobContext ctx)
    {
        if (!ctx.WaitClosed("OUTLOOK", "Outlook")) return "Passé : Outlook resté ouvert";
        var parts = new List<string>();
        var signatures = Path.Combine(ctx.Layout.Outlook, "Signatures");
        if (Directory.Exists(signatures))
        {
            var result = Tools.Robocopy(signatures, Path.Combine(ctx.Profile.Roaming, @"Microsoft\Signatures"), ctx.Log, keepNewer: true);
            parts.Add($"signatures remises ({result.Source.Files} fichiers)");
        }
        var pstFolder = Path.Combine(ctx.Layout.Outlook, "PST");
        var pst = new List<string>();
        if (Directory.Exists(pstFolder) && ctx.Profile.FolderByKey("Documents") is { } docs)
        {
            var target = Path.Combine(docs.Path, "Fichiers Outlook");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(pstFolder, "*.pst"))
            {
                var destination = Path.Combine(target, Path.GetFileName(file));
                if (File.Exists(destination)) destination = Path.Combine(target, $"{Path.GetFileNameWithoutExtension(file)} (migration).pst");
                ctx.Log.Report($"    {Path.GetFileName(file)}…");
                File.Copy(file, destination, overwrite: false);
                pst.Add(destination);
            }
            if (pst.Count > 0)
            {
                parts.Add($"{pst.Count} fichiers .pst dans {target}");
                pst.Add("Dans Outlook : Fichier > Ouvrir et exporter > Ouvrir un fichier de données Outlook, puis choisir le .pst.");
            }
        }
        ctx.Report.Detail("Fichiers Outlook (.pst)", pst);
        return parts.Count == 0 ? "Rien à restaurer" : string.Join(", ", parts);
    }

    // ---------- Fond d'écran et imprimantes ----------

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(int action, int param, string value, int flags);

    private static string Wallpaper(JobContext ctx)
    {
        var source = Directory.GetFiles(ctx.Layout.Wallpaper).FirstOrDefault();
        if (source is null) return "Pas d'image dans la sauvegarde";
        var pictures = ctx.Profile.FolderByKey("Pictures")?.Path ?? ctx.Profile.Path;
        Directory.CreateDirectory(pictures);
        var target = Path.Combine(pictures, "Fond d'écran (migration)" + Path.GetExtension(source));
        File.Copy(source, target, overwrite: true);
        try
        {
            using var key = Registry.Users.OpenSubKey(ctx.Profile.Sid + @"\Control Panel\Desktop", writable: true);
            key?.SetValue("WallPaper", target);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        // SPI_SETDESKWALLPAPER, mis à jour et prévenu tout de suite (sinon : à la prochaine ouverture de session).
        if (ctx.Profile.IsProcessUser && SystemParametersInfo(20, 0, target, 3)) return "Remis";
        return "Remis (visible à la prochaine ouverture de session)";
    }

    private static string Printers(JobContext ctx)
    {
        var lines = File.ReadAllLines(ctx.Layout.PrintersFile, Encoding.UTF8).Where(l => l.Trim().Length > 0).ToList();
        ctx.Report.Detail("Imprimantes à réinstaller (pilote du fabricant)", lines);
        return lines.Count == 0 ? "Aucune imprimante sur l'ancien PC" : $"{lines.Count} à réinstaller avec le pilote du fabricant (liste dans le rapport)";
    }
}
