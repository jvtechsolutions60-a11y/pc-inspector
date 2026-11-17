using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace JvtechEntretien;

/// <summary>
/// Accès au dossier partagé du NAS du client. La tâche tourne en compte SYSTEM, que le NAS ne connaît pas : la
/// connexion se fait avec le compte du NAS donné dans entretien.ini, puis elle est fermée.
/// </summary>
internal static class Nas
{
    /// <summary>
    /// Se connecte au partage, crée le dossier puis lance le travail. Renvoie null si tout est fait, sinon la raison
    /// de l'échec. La connexion est toujours fermée ensuite.
    /// </summary>
    public static string? Ecrire(Reglages reglages, Action<string> travail)
    {
        var dossier = reglages.DossierPartage;
        if (dossier.Length == 0) return "dossier_partage vide dans entretien.ini";
        var partage = Racine(dossier);
        if (partage is null) return $@"chemin du NAS invalide : {dossier} (attendu : \\NAS\partage\dossier)";
        var connecte = false;
        if (reglages.PartageUtilisateur.Length > 0)
        {
            var erreur = Connecter(partage, reglages.PartageUtilisateur, reglages.PartageMotDePasse);
            if (erreur is not null) return $"connexion à {partage} impossible : {erreur}";
            connecte = true;
        }
        try
        {
            Directory.CreateDirectory(dossier);
            travail(dossier);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
        finally
        {
            if (connecte) WNetCancelConnection2(partage, 0, true);
        }
    }

    /// <summary>Vérifie à l'installation que le PC peut écrire dans le dossier du NAS (fichier d'essai écrit puis effacé).</summary>
    public static string? Tester(Reglages reglages) => Ecrire(reglages, dossier =>
    {
        var essai = Path.Combine(dossier, $"essai-{reglages.Poste}.tmp");
        File.WriteAllText(essai, "essai JVTECH Entretien");
        File.Delete(essai);
    });

    /// <summary>« \\NAS\JVTECH\Entretien » donne « \\NAS\JVTECH » (le partage auquel se connecter).</summary>
    internal static string? Racine(string chemin)
    {
        if (!chemin.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        var parties = chemin.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return parties.Length >= 2 ? $@"\\{parties[0]}\{parties[1]}" : null;
    }

    /// <summary>
    /// Ajoute des lignes à la fin d'un fichier (avec l'en-tête si le fichier est nouveau). Plusieurs PC peuvent écrire
    /// en même temps à l'ouverture de session : le fichier est ouvert en exclusif et l'écriture réessayée.
    /// </summary>
    public static void AjouterLignes(string fichier, string? entete, string lignes)
    {
        for (var essai = 1; ; essai++)
        {
            try
            {
                using var flux = new FileStream(fichier, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var nouveau = flux.Length == 0;
                flux.Seek(0, SeekOrigin.End);
                // BOM UTF-8 en tête d'un nouveau fichier : Excel lit alors les accents correctement.
                using var ecriture = new StreamWriter(flux, nouveau ? new UTF8Encoding(true) : new UTF8Encoding(false));
                if (nouveau && entete is not null) ecriture.Write(entete + "\r\n");
                ecriture.Write(lignes);
                return;
            }
            catch (IOException) when (essai < 10)
            {
                Thread.Sleep(TimeSpan.FromSeconds(2 * essai));
            }
        }
    }

    private static string? Connecter(string partage, string utilisateur, string motDePasse)
    {
        var ressource = new NetResource { Type = 1 /* RESOURCETYPE_DISK */, RemoteName = partage };
        var code = WNetAddConnection2(ressource, motDePasse, utilisateur, 0);
        return code switch
        {
            0 => null,
            // Déjà connecté : on tente d'écrire quand même.
            1219 or 85 => null,
            86 or 1326 => "identifiant ou mot de passe du NAS refusé",
            53 or 67 => "NAS introuvable sur le réseau (allumé ? même réseau ?)",
            _ => new Win32Exception(code).Message,
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NetResource ressource, string? motDePasse, string? utilisateur, int options);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string nom, int options, bool forcer);
}
