using System;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using PcInspector;

namespace JvtechEntretien;

/// <summary>
/// Installation : copie dans C:\Program Files\JVTECH\Entretien, entretien.ini lisible seulement par SYSTEM et les
/// administrateurs (il contient le mot de passe du NAS), et une tâche planifiée « \JVTECH\Entretien » lancée
/// 5 minutes après chaque ouverture de session, en compte SYSTEM, cachée : entretien complet à chaque fois.
/// </summary>
internal static class Installation
{
    public const string Tache = @"\JVTECH\Entretien";

    public static string Dossier => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JVTECH", "Entretien");

    private static bool EstAdministrateur()
    {
        using var identite = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identite).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Installer(Action<string> dire)
    {
        if (!EstAdministrateur()) { dire("Installation impossible : lancer en administrateur."); return 1; }
        var source = AppPaths.ExeFolder;
        var ini = Path.Combine(source, "entretien.ini");
        if (!File.Exists(ini)) { dire($"entretien.ini introuvable à côté du .exe ({source}) : le remplir avant d'installer."); return 1; }
        var reglages = Reglages.Charger(ini);
        foreach (var ligne in reglages.Ignorees) dire($"entretien.ini : ligne ignorée « {ligne} »");

        if (reglages.DossierPartage.Length == 0) dire("Attention : dossier_partage est vide, les rapports resteront sur le PC.");
        else
        {
            dire($"Essai d'écriture sur le NAS ({reglages.DossierPartage})…");
            var erreur = Nas.Tester(reglages);
            if (erreur is not null) { dire("Écriture sur le NAS impossible : " + erreur); dire("Corriger entretien.ini puis relancer --installer."); return 1; }
            dire("NAS : écriture possible.");
        }

        Directory.CreateDirectory(Dossier);
        var exe = Path.Combine(Dossier, "JvtechEntretien.exe");
        var iniInstalle = Path.Combine(Dossier, "entretien.ini");
        if (!MemeDossier(source, Dossier))
        {
            File.Copy(Path.Combine(source, "JvtechEntretien.exe"), exe, overwrite: true);
            File.Copy(ini, iniInstalle, overwrite: true);
        }
        // Réglages lisibles seulement par SYSTEM et les administrateurs (SID, pour toutes les langues de Windows).
        var droits = ProcessRunner.Run("icacls.exe", $"\"{iniInstalle}\" /inheritance:r /grant:r *S-1-5-18:F *S-1-5-32-544:F", null, TimeSpan.FromSeconds(30));
        if (droits.ExitCode != 0) dire("Attention : impossible de protéger entretien.ini (icacls).");

        var xml = Path.Combine(Path.GetTempPath(), "JvtechEntretien-tache.xml");
        File.WriteAllText(xml, TacheXml(exe, Dossier), Encoding.Unicode);
        try
        {
            var sortie = new StringBuilder();
            var resultat = ProcessRunner.Run("schtasks.exe", $"/Create /TN \"{Tache}\" /XML \"{xml}\" /F", l => sortie.AppendLine(l), TimeSpan.FromSeconds(30));
            if (resultat.ExitCode != 0) { dire("Création de la tâche planifiée impossible : " + sortie.ToString().Trim()); return 1; }
        }
        finally { try { File.Delete(xml); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }

        dire($"JVTECH Entretien installé dans {Dossier}.");
        dire("Tâche « JVTECH\\Entretien » : 5 minutes après chaque ouverture de session, en compte SYSTEM, sans fenêtre.");
        dire("Entretien à chaque ouverture de session. Pour l'entretien tout de suite : JvtechEntretien.exe --maintenant");
        Suivi.Journal("Installation (" + AppInfo.Version + ")");
        return 0;
    }

    public static int Desinstaller(Action<string> dire)
    {
        if (!EstAdministrateur()) { dire("Désinstallation impossible : lancer en administrateur."); return 1; }
        ProcessRunner.Run("schtasks.exe", $"/Delete /TN \"{Tache}\" /F", null, TimeSpan.FromSeconds(30));
        SupprimerDossier(Suivi.Dossier);
        if (MemeDossier(AppPaths.ExeFolder, Dossier))
            ProcessRunner.Run("cmd.exe", $"/c start \"\" /min cmd.exe /c \"ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{Dossier}\"\"", null, TimeSpan.FromSeconds(10));
        else SupprimerDossier(Dossier);
        dire("JVTECH Entretien désinstallé (les rapports déjà déposés sur le NAS sont gardés).");
        return 0;
    }

    /// <summary>
    /// Tâche planifiée : à l'ouverture de session de n'importe quel compte, 5 minutes plus tard (le temps que le réseau
    /// soit prêt), en compte SYSTEM, cachée, priorité basse, 4 heures au plus. Jamais de réveil ni de redémarrage.
    /// </summary>
    internal static string TacheXml(string exe, string dossier) => $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>JVTECH Solutions</Author>
    <Description>JVTECH Entretien : mises à jour Windows et logiciels à chaque ouverture de session, rapport déposé sur le NAS. Ne redémarre jamais le PC.</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT5M</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT4H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exe)}</Command>
      <Arguments>--tache</Arguments>
      <WorkingDirectory>{SecurityElement.Escape(dossier)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
";

    private static bool MemeDossier(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void SupprimerDossier(string dossier)
    {
        try { if (Directory.Exists(dossier)) Directory.Delete(dossier, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
