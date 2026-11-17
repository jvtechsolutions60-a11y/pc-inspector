using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PcInspector.Collectors;

/// <summary>Un nom ou une adresse trouvés sur le PC, avec l'endroit où ils ont été lus.</summary>
public sealed record IdentityValue(string Value, string Source);

/// <summary>
/// Nom, prénom et adresse e-mail de l'utilisateur du PC, pour l'en-tête du rapport (au lieu d'ouvrir un nouveau mail
/// dans Outlook). Lecture seule : registre de l'utilisateur et WMI, rien n'est modifié.
/// PC Inspector tourne en administrateur : on lit la session ouverte à l'écran (le client), pas le compte qui a
/// autorisé l'élévation, qui peut être celui du technicien.
/// </summary>
public static class UserIdentity
{
    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public sealed class Result
    {
        public List<IdentityValue> Names { get; } = new();
        public List<IdentityValue> Emails { get; } = new();

        internal void AddName(string? value, string source)
        {
            value = value?.Trim();
            if (string.IsNullOrWhiteSpace(value) || Email.IsMatch(value!)) return;
            if (!Names.Any(n => n.Value.Equals(value, StringComparison.OrdinalIgnoreCase))) Names.Add(new IdentityValue(value!, source));
        }

        internal void AddEmail(string? value, string source)
        {
            value = value?.Trim();
            if (string.IsNullOrWhiteSpace(value) || !Email.IsMatch(value!)) return;
            if (!Emails.Any(e => e.Value.Equals(value, StringComparison.OrdinalIgnoreCase))) Emails.Add(new IdentityValue(value!.ToLowerInvariant(), source));
        }
    }

    public static Result Read()
    {
        var result = new Result();
        var (account, sid) = SessionUser();
        using var hive = OpenUserHive(sid);

        // 1. Comptes ajoutés dans Office / Outlook : nom affiché et adresse, comme dans « De : » d'un nouveau mail.
        Try(() => Office(hive, result));
        // 2. Nom complet du compte Windows (pour un compte Microsoft, le nom du compte).
        Try(() => result.AddName(FullName(sid, account), "compte Windows"));
        // 3. Compte Microsoft connecté à Windows.
        Try(() => MicrosoftAccount(hive, result));
        // 4. Compte professionnel ou scolaire (Entra ID) ajouté au PC.
        Try(() => WorkAccounts(hive, result));
        // 5. Nom saisi à l'installation d'Office.
        Try(() => result.AddName(Value(hive, @"Software\Microsoft\Office\Common\UserInfo", "UserName"), "Office (nom d'utilisateur)"));
        return result;
    }

    private static void Office(RegistryKey? hive, Result result)
    {
        foreach (var version in new[] { "16.0", "15.0" })
        {
            using var identities = hive?.OpenSubKey($@"Software\Microsoft\Office\{version}\Common\Identity\Identities");
            if (identities is null) continue;
            foreach (var name in identities.GetSubKeyNames())
            {
                using var id = identities.OpenSubKey(name);
                if (id is null) continue;
                result.AddEmail(id.GetValue("EmailAddress") as string ?? id.GetValue("SignInName") as string, "Office / Outlook");
                result.AddName(id.GetValue("FriendlyName") as string, "Office / Outlook");
            }
        }
    }

    private static void MicrosoftAccount(RegistryKey? hive, Result result)
    {
        using var key = hive?.OpenSubKey(@"Software\Microsoft\IdentityCRL\UserExtendedProperties");
        foreach (var name in key?.GetSubKeyNames() ?? Array.Empty<string>()) result.AddEmail(name, "compte Microsoft");
    }

    private static void WorkAccounts(RegistryKey? hive, Result result)
    {
        using var join = hive?.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\WorkplaceJoin\JoinInfo");
        foreach (var name in join?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var k = join!.OpenSubKey(name);
            result.AddEmail(k?.GetValue("UserEmail") as string, "compte professionnel");
        }
        using var cloud = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
        foreach (var name in cloud?.GetSubKeyNames() ?? Array.Empty<string>())
        {
            using var k = cloud!.OpenSubKey(name);
            result.AddEmail(k?.GetValue("UserEmail") as string, "PC joint à Entra ID");
        }
    }

    /// <summary>Compte de la session ouverte à l'écran (DOMAINE\nom) et son SID ; à défaut, le compte qui lance le logiciel.</summary>
    private static (string? Account, string? Sid) SessionUser()
    {
        string? account = null;
        try { account = Wmi.QueryOptional("SELECT UserName FROM Win32_ComputerSystem").FirstOrDefault()?.Str("UserName"); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Management.ManagementException) { }
        if (string.IsNullOrWhiteSpace(account)) return (Environment.UserName, WindowsIdentity.GetCurrent().User?.Value);
        try { return (account, ((SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier))).Value); }
        catch (SystemException) { return (account, null); }
    }

    private static RegistryKey? OpenUserHive(string? sid)
    {
        try
        {
            if (sid is not null && Registry.Users.OpenSubKey(sid) is { } hive) return hive;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
        return Registry.CurrentUser.OpenSubKey("");
    }

    private static string? FullName(string? sid, string? account)
    {
        if (sid is null) return null;
        var user = Wmi.QueryOptional($"SELECT FullName FROM Win32_UserAccount WHERE SID = '{sid.Replace("'", "")}'").FirstOrDefault();
        var name = user?.Str("FullName");
        // Nom complet vide ou identique à l'identifiant : rien d'utile.
        var login = account?.Split('\\').Last();
        return string.IsNullOrWhiteSpace(name) || name!.Equals(login, StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    private static string? Value(RegistryKey? hive, string path, string name)
    {
        using var key = hive?.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }

    // Une source illisible n'empêche pas les autres.
    private static void Try(Action read)
    {
        try { read(); }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException
                                       or System.Management.ManagementException or InvalidOperationException) { }
    }
}
