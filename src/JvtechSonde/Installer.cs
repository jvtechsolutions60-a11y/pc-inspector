using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using PcInspector;

namespace JvtechSonde;

/// <summary>
/// Installation et désinstallation : copie dans C:\Program Files\JVTECH\Sonde (dossier protégé en écriture) et une
/// tâche planifiée « \JVTECH\Sonde » lancée en compte SYSTEM chaque jour vers 12 h et 5 minutes après le démarrage.
/// Pas de service ni de programme qui tourne en permanence.
/// </summary>
internal static class Installer
{
    public const string TaskName = @"\JVTECH\Sonde";

    public static string InstallFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "JVTECH", "Sonde");

    public static string InstalledExe => Path.Combine(InstallFolder, "JvtechSonde.exe");

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static int Install(Action<string> say)
    {
        if (!IsAdministrator()) { say("Installation impossible : lancer la sonde en administrateur."); return 1; }

        var source = AppPaths.ExeFolder;
        var ini = Path.Combine(source, "sonde.ini");
        if (!File.Exists(ini)) { say($"sonde.ini introuvable à côté du .exe ({source}) : le remplir avant d'installer."); return 1; }
        var settings = Settings.Load(ini);
        if (string.IsNullOrWhiteSpace(settings.KumaPush))
            say("Attention : kuma_push est vide dans sonde.ini, la sonde mesurera sans rien envoyer.");

        Directory.CreateDirectory(InstallFolder);
        if (!SameFolder(source, InstallFolder))
        {
            File.Copy(Path.Combine(source, "JvtechSonde.exe"), InstalledExe, overwrite: true);
            File.Copy(ini, Path.Combine(InstallFolder, "sonde.ini"), overwrite: true);
            var modules = Path.Combine(source, "capteurs");
            if (Directory.Exists(modules)) CopyFolder(modules, Path.Combine(InstallFolder, "capteurs"));
        }

        var xml = Path.Combine(Path.GetTempPath(), "JvtechSonde-tache.xml");
        // Le Planificateur de tâches attend du XML en UTF-16.
        File.WriteAllText(xml, TaskXml(InstalledExe, InstallFolder), Encoding.Unicode);
        try
        {
            var output = new StringBuilder();
            var result = ProcessRunner.Run("schtasks.exe", $"/Create /TN \"{TaskName}\" /XML \"{xml}\" /F", l => output.AppendLine(l), TimeSpan.FromSeconds(30));
            if (result.ExitCode != 0) { say("Création de la tâche planifiée impossible : " + output.ToString().Trim()); return 1; }
        }
        finally { TryDelete(xml); }

        say($"Sonde installée dans {InstallFolder}.");
        say("Tâche planifiée « JVTECH\\Sonde » : chaque jour vers 12 h et 5 minutes après chaque démarrage, en compte SYSTEM.");
        say("Premier passage maintenant…");
        ProcessRunner.Run("schtasks.exe", $"/Run /TN \"{TaskName}\"", null, TimeSpan.FromSeconds(30));
        Results.Log("Installation (" + AppInfo.Version + ")");
        return 0;
    }

    public static int Uninstall(Action<string> say)
    {
        if (!IsAdministrator()) { say("Désinstallation impossible : lancer la sonde en administrateur."); return 1; }

        ProcessRunner.Run("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", null, TimeSpan.FromSeconds(30));
        // Le dossier \JVTECH du Planificateur reste s'il ne contient plus rien : sans effet, et il peut servir à d'autres outils.
        TryDeleteFolder(Results.DataFolder);

        if (SameFolder(AppPaths.ExeFolder, InstallFolder))
        {
            // Un .exe ne peut pas se supprimer lui-même : le dossier est effacé quelques secondes après la fin du programme.
            ProcessRunner.Run("cmd.exe", $"/c start \"\" /min cmd.exe /c \"ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{InstallFolder}\"\"", null, TimeSpan.FromSeconds(10));
        }
        else
        {
            TryDeleteFolder(InstallFolder);
        }
        say("Sonde désinstallée : tâche planifiée, programme et résultats supprimés.");
        return 0;
    }

    /// <summary>Définition de la tâche planifiée (format XML du Planificateur de tâches).</summary>
    internal static string TaskXml(string exe, string folder) => $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Author>JVTECH Solutions</Author>
    <Description>JVTECH Sonde : contrôle quotidien de l'état du PC (lecture seule), envoyé à JVTECH. Ne modifie rien sur le PC.</Description>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>2026-01-01T12:00:00</StartBoundary>
      <Enabled>true</Enabled>
      <RandomDelay>PT15M</RandomDelay>
      <ScheduleByDay>
        <DaysInterval>1</DaysInterval>
      </ScheduleByDay>
    </CalendarTrigger>
    <BootTrigger>
      <Enabled>true</Enabled>
      <Delay>PT5M</Delay>
    </BootTrigger>
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
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{SecurityElement.Escape(exe)}</Command>
      <Arguments>--mesurer</Arguments>
      <WorkingDirectory>{SecurityElement.Escape(folder)}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>
";

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
