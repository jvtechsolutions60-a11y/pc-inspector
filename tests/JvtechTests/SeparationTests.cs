using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Jvtech.Lecture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JvtechTests;

/// <summary>
/// JVTECH v2 : séparation lecture / action, vérifiée à chaque compilation.
/// - La bibliothèque commune JvtechLecture et JVTECH Audit ne contiennent aucun appel qui modifie Windows.
/// - JVTECH Audit n'inclut pas JvtechAtelier.Actions ni le code des logiciels qui agissent (v1).
/// - JVTECH Audit ne demande pas les droits administrateur.
/// Ces tests lisent le code source du dépôt (fichiers propres au projet + fichiers liés dans le .csproj).
/// Ils ne remplacent pas l'essai réel T22 (export du registre avant/après sur un PC de test).
/// </summary>
[TestClass]
public class SeparationTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PcInspector.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable (PcInspector.sln)");
    }

    /// <summary>Appels qui modifient Windows ou écrivent sur le disque : interdits en lecture seule.</summary>
    internal static readonly (string Nom, Regex Motif)[] Interdits =
    {
        ("écriture registre", new Regex(@"\b(CreateSubKey|DeleteSubKey|DeleteSubKeyTree|DeleteValue)\b|Registry\.SetValue|RegistryKey[^;]*\.SetValue|OpenSubKey\([^)]*,\s*true\s*\)|RegistryKeyPermissionCheck\.ReadWriteSubTree")),
        ("service Windows", new Regex(@"\bServiceController\b|System\.ServiceProcess")),
        ("méthode WMI", new Regex(@"\.InvokeMethod\(|\.Put\(\s*\)|\bDeleteInstance\b")),
        ("lancement de programme", new Regex(@"Process\.Start|new\s+ProcessStartInfo|\bProcessRunner\b")),
        ("écriture de fichier", new Regex(@"File\.(Write|Append|Delete|Move|Copy|Create|Replace|SetAttributes|Encrypt)\w*|Directory\.(Create|Delete|Move)\w*|new\s+(FileStream|StreamWriter)\b|(FileInfo|DirectoryInfo)\b[^;]*\.(Delete|MoveTo|CopyTo|Create)\(|\.Delete\(\s*(true|false)?\s*\)")),
        ("outil système qui modifie", new Regex(@"\b(powercfg|bcdedit|reg(\.exe)?\s+(add|delete|import)|sc(\.exe)?\s+(config|stop|delete)|netsh|sysprep|dism|wusa|winget\s+(install|upgrade|uninstall))\b", RegexOptions.IgnoreCase)),
    };

    internal static IEnumerable<string> Violations(string code)
    {
        var lines = code.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")) continue;
            foreach (var (nom, motif) in Interdits)
                if (motif.IsMatch(line))
                    yield return $"ligne {i + 1} ({nom}) : {line}";
        }
    }

    private static string ProjectFile(string project) => Path.Combine(Root, "src", project, project + ".csproj");

    /// <summary>Fichiers .cs compilés dans un projet : ceux de son dossier et ceux liés depuis d'autres dossiers.</summary>
    private static List<string> SourceFiles(string project)
    {
        var dir = Path.Combine(Root, "src", project);
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .ToList();
        foreach (var include in CompileIncludes(project))
        {
            var relative = include.Replace('\\', Path.DirectorySeparatorChar);
            var glob = relative.IndexOf("**", StringComparison.Ordinal);
            if (glob >= 0)
            {
                // Chemin avant « ** » seulement : les jokers ne passent pas dans GetFullPath.
                var baseDir = Path.GetFullPath(Path.Combine(dir, relative.Substring(0, glob).TrimEnd(Path.DirectorySeparatorChar)));
                files.AddRange(Directory.GetFiles(baseDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)));
            }
            else files.Add(Path.GetFullPath(Path.Combine(dir, relative)));
        }
        return files.Distinct().ToList();
    }

    private static IEnumerable<string> CompileIncludes(string project) =>
        XDocument.Load(ProjectFile(project)).Descendants()
            .Where(e => e.Name.LocalName is "Compile" or "ProjectReference" or "Reference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(i => i is not null && i.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(i => i!);

    private static IEnumerable<string> AllIncludes(string project) =>
        XDocument.Load(ProjectFile(project)).Descendants()
            .Select(e => (string?)e.Attribute("Include"))
            .Where(i => i is not null)
            .Select(i => i!);

    [TestMethod]
    public void Detecteur_RepereLesEcritures()
    {
        Assert.IsTrue(Violations("key.DeleteValue(\"X\");").Any());
        Assert.IsTrue(Violations("using var k = Registry.LocalMachine.OpenSubKey(p, true);").Any());
        Assert.IsTrue(Violations("File.WriteAllText(p, t);").Any());
        Assert.IsTrue(Violations("Process.Start(\"powercfg\", \"/h off\");").Any());
        Assert.IsTrue(Violations("new ServiceController(\"wuauserv\").Stop();").Any());
        Assert.IsTrue(Violations("new FileInfo(p).Delete();").Any());
        Assert.IsTrue(Violations("dir.Delete(true);").Any());
        Assert.IsFalse(Violations("using var k = Registry.LocalMachine.OpenSubKey(p);").Any());
        Assert.IsFalse(Violations("BitConverter.GetBytes(value).CopyTo(request, offset);").Any(), "copie de tableau en mémoire");
        Assert.IsFalse(Violations("// File.WriteAllText est interdit ici").Any());
    }

    [DataTestMethod]
    [DataRow("JvtechLecture")]
    [DataRow("JvtechAudit")]
    public void LectureSeule_AucunAppelQuiModifie(string project)
    {
        // Seule exception : l'export du rapport d'Audit, à l'endroit choisi par le technicien (vérifié à part ci-dessous).
        var files = SourceFiles(project).Where(f => !(project == "JvtechAudit" && Path.GetFileName(f) == ExportAudit)).ToList();
        Assert.IsTrue(files.Count > 0, "aucun fichier trouvé pour " + project);
        var problems = files.SelectMany(f => Violations(File.ReadAllText(f)).Select(v => Path.GetFileName(f) + " " + v)).ToList();
        Assert.AreEqual(0, problems.Count, string.Join(Environment.NewLine, problems));
    }

    private const string ExportAudit = "ExportRapport.cs";

    [TestMethod]
    public void Audit_SeuleEcriture_LeRapportChoisiParLeTechnicien()
    {
        var chemin = Path.Combine(Root, "src", "JvtechAudit", ExportAudit);
        var code = File.ReadAllText(chemin);
        var ecritures = Violations(code).ToList();
        Assert.AreEqual(1, ecritures.Count, string.Join(Environment.NewLine, ecritures));
        StringAssert.Contains(ecritures[0], "File.WriteAllText(dialogue.FileName");
        StringAssert.Contains(code, "new SaveFileDialog");
        StringAssert.Contains(code, "if (dialogue.ShowDialog(proprietaire) != true) return null;");
        // Personne d'autre dans Audit n'ouvre de fenêtre « Enregistrer sous » ni n'écrit de fichier.
        var autres = SourceFiles("JvtechAudit").Where(f => Path.GetFileName(f) != ExportAudit)
            .Where(f => File.ReadAllText(f).Contains("SaveFileDialog")).Select(Path.GetFileName).ToList();
        Assert.AreEqual(0, autres.Count, string.Join(", ", autres));
    }

    [TestMethod]
    public void Audit_NIncluePasLesActions()
    {
        var includes = AllIncludes("JvtechAudit").ToList();
        foreach (var interdit in new[] { "JvtechAtelier", "PcMaintenance", "PcMigration", "JvtechServeur", "ProcessRunner", "CrashGuard", "AppInfo.cs" })
            Assert.IsFalse(includes.Any(i => i.IndexOf(interdit, StringComparison.OrdinalIgnoreCase) >= 0),
                $"JVTECH Audit ne doit pas inclure {interdit}");
        Assert.IsTrue(includes.Any(i => i.Contains("JvtechLecture")), "Audit doit compiler la bibliothèque commune");
    }

    [TestMethod]
    public void Lecture_NeDependPasDesActions()
    {
        var includes = AllIncludes("JvtechLecture").ToList();
        Assert.IsFalse(includes.Any(i => i.IndexOf("Atelier", StringComparison.OrdinalIgnoreCase) >= 0));
        var refs = typeof(Statut).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        foreach (var interdit in new[] { "JvtechAtelier.Actions", "System.ServiceProcess", "PresentationFramework", "System.Configuration.Install" })
            CollectionAssert.DoesNotContain(refs, interdit);
    }

    [TestMethod]
    public void Atelier_InclutLecturePuisActions()
    {
        var includes = AllIncludes("JvtechAtelier").ToList();
        Assert.IsTrue(includes.Any(i => i.Contains("JvtechLecture")));
        Assert.IsTrue(includes.Any(i => i.Contains("JvtechAtelier.Actions")));
    }

    [TestMethod]
    public void Audit_SansDroitsAdministrateur()
    {
        var csproj = XDocument.Load(ProjectFile("JvtechAudit"));
        var manifest = csproj.Descendants().Single(e => e.Name.LocalName == "ApplicationManifest").Value;
        var text = File.ReadAllText(Path.Combine(Root, "src", "JvtechAudit", manifest));
        StringAssert.Contains(text, "level=\"asInvoker\"");
        Assert.IsFalse(text.Contains("requireAdministrator") || text.Contains("highestAvailable"));
    }
}
