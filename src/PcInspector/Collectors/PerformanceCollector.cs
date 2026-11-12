using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using PcInspector.Models;

namespace PcInspector.Collectors;

/// <summary>
/// Onglet « Performances » : affiche les derniers résultats des tests, lancés seulement à la demande (boutons de
/// l'onglet). Le rapport et la fiche reprennent ces résultats sans relancer de test.
/// </summary>
public sealed class PerformanceCollector : ICollector
{
    public string Title => "Performances";
    public string Icon => "";

    public InfoSection Collect() => Benchmark.Last?.Section ?? NotRun();

    private InfoSection NotRun()
    {
        var section = new InfoSection(Title);
        section.Groups.Add(new InfoGroup("Tests de performance")
            .Add("Résultat", "Pas encore testé : le test bureautique se lance tout seul après le bilan")
            .Add("Test bureautique", "Environ 3 minutes : notes Windows (WinSAT), processeur (7-Zip), disque")
            .Add("Test gaming complet", "Environ 20 minutes : en plus, Cinebench, Blender (carte graphique) et 10 minutes de charge avec les températures")
            .Add("Outils gaming", $"Facultatifs : Cinebench et Blender Benchmark se déposent une fois dans le dossier {Benchmark.ToolsFolder} (sans Cinebench, le score processeur vient de 7-Zip)"));
        return section;
    }
}

/// <summary>Résultats d'une série de tests, réutilisés par l'onglet, le rapport, la fiche et le Texte Leboncoin.</summary>
public sealed class BenchmarkResult
{
    public BenchmarkResult(InfoSection section) => Section = section;
    public InfoSection Section { get; }
    public bool Gaming { get; set; }
    public string? OfficeVerdict { get; set; }
    public string? GamingVerdict { get; set; }
    public string? CinebenchMulti { get; set; }
    public string? CinebenchSingle { get; set; }
    public string? BlenderScore { get; set; }
    public string? SevenZip { get; set; }

    /// <summary>Une ligne pour la fiche de suivi et le Texte Leboncoin.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (OfficeVerdict is not null) parts.Add("Bureautique : " + OfficeVerdict);
            if (GamingVerdict is not null) parts.Add("Jeux : " + GamingVerdict);
            if (CinebenchMulti is not null) parts.Add($"Cinebench {CinebenchMulti} pts multi" + (CinebenchSingle is null ? "" : $" / {CinebenchSingle} mono"));
            if (BlenderScore is not null) parts.Add($"Blender {BlenderScore}");
            if (CinebenchMulti is null && SevenZip is not null) parts.Add($"7-Zip {SevenZip} MIPS");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Tests de performance. Que de la mesure : rien n'est installé ni réglé. Outils utilisés, tous gratuits pour un
/// atelier : WinSAT (dans Windows), 7-Zip (7zr.exe, LGPL, embarqué), un test d'écriture disque maison, et pour le
/// test gaming Cinebench (Maxon) et Blender Benchmark (GPL), que le technicien dépose dans le dossier Outils.
/// </summary>
public static class Benchmark
{
    public static BenchmarkResult? Last { get; private set; }

    /// <summary>Dossier « Outils » à côté de PcInspector.exe (ou sur la clé USB).</summary>
    public static string ToolsFolder => Path.Combine(AppPaths.ExeFolder, "Outils");

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // Partie bureautique du dernier test : le test gaming la reprend au lieu de la refaire.
    private static List<InfoGroup>? _officeGroups;
    private static WinSatScores? _officeWinSat;
    private static string? _officeSevenZip;

    public static BenchmarkResult Run(bool gaming, Action<string> progress)
    {
        var section = new InfoSection("Performances");
        var result = new BenchmarkResult(section) { Gaming = gaming };
        var summary = new InfoGroup(gaming ? "Verdict (test gaming complet)" : "Verdict (test bureautique)");
        section.Groups.Add(summary);

        WinSatScores winsat;
        if (gaming && _officeGroups is not null && _officeWinSat is not null)
        {
            // Le test bureautique vient d'être fait (au lancement du logiciel) : ses mesures sont reprises.
            section.Groups.AddRange(_officeGroups);
            winsat = _officeWinSat;
            result.SevenZip = _officeSevenZip;
        }
        else
        {
            progress("Notes Windows (WinSAT) : environ 2 minutes…");
            winsat = WinSat(section);

            progress("Processeur : test 7-Zip…");
            SevenZip(section, result);

            progress("Disque : test d'écriture…");
            DiskWrite(section, winsat);

            _officeGroups = section.Groups.Skip(1).ToList();
            _officeWinSat = winsat;
            _officeSevenZip = result.SevenZip;
        }

        result.OfficeVerdict = OfficeVerdict(winsat, out var officeAdvice);
        summary.Add("Bureautique", result.OfficeVerdict ?? "Notes Windows indisponibles");
        if (officeAdvice is not null) summary.Add("Conseil", officeAdvice);

        if (gaming)
        {
            var load = new InfoGroup("Charge de 10 minutes (stabilité et refroidissement)");
            CinebenchAndLoad(section, result, load, progress);
            progress("Carte graphique : Blender Benchmark (téléchargement la première fois)…");
            var blender = BlenderBench(section, result, progress);
            section.Groups.Add(load);
            result.GamingVerdict = GamingVerdict(blender, winsat);
            summary.Add("Jeux", result.GamingVerdict);
            // Un score de jeu ne vaut que si la carte a un pilote récent du fabricant.
            try
            {
                foreach (var driver in GpuDriver.ReadAll(DateTime.Today).Where(d => d.Vendor is "NVIDIA" or "AMD" or "Windows"))
                    summary.Add("Pilote graphique", $"{driver.Vendor} : {driver.Message}");
            }
            catch (Exception ex) when (ex is System.Management.ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException) { }
            if (load.Items.FirstOrDefault(i => i.Label == "Verdict") is { } heat) summary.Add("Refroidissement", heat.Value);
        }

        summary.Add("Date du test", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
        Last = result;
        return result;
    }

    // ---------- WinSAT ----------

    private sealed class WinSatScores
    {
        public double? Cpu, Memory, Disk, Graphics, D3D, Base;
        public double? SeqRead, RandomRead, MemoryBandwidth;
    }

    private static WinSatScores WinSat(InfoSection section)
    {
        var scores = new WinSatScores();
        var run = ProcessRunner.Run("winsat.exe", "formal", null, TimeSpan.FromMinutes(6));
        var group = new InfoGroup("Notes Windows (WinSAT, de 1 à 9,9)");

        var row = Wmi.QueryOptional("SELECT * FROM Win32_WinSAT").FirstOrDefault();
        if (row is not null)
        {
            double? Score(string name) => double.TryParse(row.Str(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
            scores.Cpu = Score("CPUScore");
            scores.Memory = Score("MemoryScore");
            scores.Disk = Score("DiskScore");
            scores.Graphics = Score("GraphicsScore");
            scores.D3D = Score("D3DScore");
            scores.Base = Score("WinSPRLevel");
        }
        ReadWinSatXml(scores);

        void Add(string label, double? value)
        {
            if (value is { } v) group.AddBar(label, v.ToString("0.0", Fr), v / 9.9, v >= 7 ? "ok" : v >= 5 ? "warning" : "critical");
        }
        Add("Processeur", scores.Cpu);
        Add("Mémoire", scores.Memory);
        Add("Disque système", scores.Disk);
        Add("Bureau (graphique)", scores.Graphics);
        Add("Jeux (Direct3D)", scores.D3D);
        if (scores.Base is { } b) group.Add("Note globale (la plus basse)", b.ToString("0.0", Fr));
        if (scores.MemoryBandwidth is { } bw) group.Add("Débit mémoire", $"{bw:0} Mo/s");
        if (group.Items.Count == 0)
            group.Add("Résultat", run.TimedOut ? "WinSAT n'a pas fini à temps" : "WinSAT indisponible sur ce PC (ou lancé sur batterie)");
        section.Groups.Add(group);
        return scores;
    }

    /// <summary>Le détail (Mo/s) est dans le dernier fichier « Formal.Assessment » de WinSAT.</summary>
    private static void ReadWinSatXml(WinSatScores scores)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Performance", "WinSAT", "DataStore");
            var file = new DirectoryInfo(folder).GetFiles("*Formal.Assessment*.xml").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
            if (file is null) return;
            var doc = XDocument.Load(file.FullName);
            double? Metric(string element, string? kind = null)
            {
                var e = doc.Descendants(element).FirstOrDefault(x => kind is null
                    || string.Equals((string?)x.Attribute("kind"), kind, StringComparison.OrdinalIgnoreCase));
                return e is not null && double.TryParse(e.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            }
            var disk = doc.Descendants("DiskMetrics").FirstOrDefault();
            if (disk is not null)
            {
                double? Disk(string kind) => disk.Elements("AvgThroughput")
                    .Where(x => string.Equals((string?)x.Attribute("kind"), kind, StringComparison.OrdinalIgnoreCase))
                    .Select(x => double.TryParse(x.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
                    .FirstOrDefault();
                scores.SeqRead = Disk("Sequential Read");
                scores.RandomRead = Disk("Random Read");
            }
            scores.MemoryBandwidth = Metric("Bandwidth");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
    }

    private static string? OfficeVerdict(WinSatScores s, out string? advice)
    {
        advice = null;
        var notes = new[] { s.Cpu, s.Memory, s.Disk }.Where(v => v is not null).Select(v => v!.Value).ToList();
        if (notes.Count == 0) return null;
        var lowest = notes.Min();
        if (s.Disk is < 6.5) advice = "Disque lent (probablement mécanique) : passer en SSD transforme le PC.";
        else if (s.Memory is < 5.5) advice = "Mémoire juste : ajouter de la RAM rendra le PC plus fluide.";
        else if (s.Cpu is < 5.5) advice = "Processeur ancien : bien pour la bureautique simple, pas pour du multitâche lourd.";
        return lowest switch
        {
            >= 7.5 => "Très fluide",
            >= 6.5 => "Fluide",
            >= 5 => "Correct pour la bureautique simple",
            _ => "Lent",
        };
    }

    // ---------- 7-Zip ----------

    private static void SevenZip(InfoSection section, BenchmarkResult result)
    {
        var group = new InfoGroup("Processeur (test 7-Zip, en MIPS)");
        var exe = ExtractTool("7zr.exe");
        if (exe is null)
        {
            group.Add("Résultat", "7-Zip n'a pas pu être préparé");
            section.Groups.Add(group);
            return;
        }
        int? Bench(string threads)
        {
            string? total = null;
            ProcessRunner.Run(exe, "b " + threads, l => { if (l.TrimStart().StartsWith("Tot:", StringComparison.Ordinal)) total = l; }, TimeSpan.FromMinutes(3));
            var last = total is null ? null : Regex.Matches(total, @"\d+").Cast<Match>().LastOrDefault();
            return last is not null && int.TryParse(last.Value, out var mips) ? mips : null;
        }
        var multi = Bench("");
        var single = Bench("-mmt1");
        if (multi is { } m)
        {
            group.Add("Tous les cœurs", m.ToString("N0", Fr));
            result.SevenZip = m.ToString("N0", Fr);
        }
        if (single is { } s) group.Add("Un seul cœur", s.ToString("N0", Fr));
        if (group.Items.Count == 0) group.Add("Résultat", "Test 7-Zip impossible");
        else group.Add("Repères", "Portable bureautique récent : 30 000 à 60 000 · PC gamer récent : 100 000 et plus");
        section.Groups.Add(group);
    }

    /// <summary>Sort un outil embarqué dans le dossier de travail (une seule fois par version de PC Inspector).</summary>
    private static string? ExtractTool(string name)
    {
        try
        {
            var folder = AppPaths.EnsureFolder(Path.Combine(AppPaths.WorkFolder, "PcInspector-" + AppInfo.Build));
            var path = Path.Combine(folder, name);
            using var resource = typeof(Benchmark).Assembly.GetManifestResourceStream(name);
            if (resource is null) return null;
            if (!File.Exists(path) || new FileInfo(path).Length != resource.Length)
            {
                using var file = File.Create(path);
                resource.CopyTo(file);
            }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---------- Disque ----------

    /// <summary>Lecture : mesure de WinSAT. Écriture : 1 Go écrits sans cache Windows, puis effacés.</summary>
    private static void DiskWrite(InfoSection section, WinSatScores winsat)
    {
        var group = new InfoGroup("Disque système (vitesses réelles)");
        if (winsat.SeqRead is { } read) group.AddBar("Lecture séquentielle", $"{read:0} Mo/s", read / 3500, read >= 400 ? "ok" : read >= 150 ? "warning" : "critical");
        if (winsat.RandomRead is { } random) group.Add("Lecture aléatoire", $"{random:0.0} Mo/s");

        var path = Path.Combine(Path.GetTempPath(), "JVTECH-test-disque.tmp");
        try
        {
            const int block = 8 * 1024 * 1024;
            const long total = 1024L * 1024 * 1024;
            var buffer = new byte[block];
            new Random(42).NextBytes(buffer); // données non compressibles
            var watch = Stopwatch.StartNew();
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                for (long written = 0; written < total; written += block) file.Write(buffer, 0, block);
                file.Flush(true);
            }
            watch.Stop();
            var mbps = total / 1048576.0 / watch.Elapsed.TotalSeconds;
            group.AddBar("Écriture séquentielle", $"{mbps:0} Mo/s", mbps / 3000, mbps >= 300 ? "ok" : mbps >= 100 ? "warning" : "critical");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            group.Add("Écriture séquentielle", "Test impossible : " + ex.Message);
        }
        finally
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        group.Add("Repères", "Disque dur mécanique : 100 à 200 Mo/s · SSD SATA : 450 à 550 Mo/s · SSD NVMe : 1 500 Mo/s et plus");
        section.Groups.Add(group);
    }

    // ---------- Cinebench et charge ----------

    private static void CinebenchAndLoad(InfoSection section, BenchmarkResult result, InfoGroup load, Action<string> progress)
    {
        var group = new InfoGroup("Processeur (Cinebench, en points)");
        var cinebench = FindTool("Cinebench.exe");
        using var sampler = new Sampler();
        if (cinebench is null)
        {
            // Cinebench est facultatif (1 Go, non redistribuable) : le score processeur vient alors de 7-Zip.
            group.Add("Résultat", $"Cinebench non utilisé : le score du processeur est celui de 7-Zip (plus haut). Facultatif : dézipper Cinebench une fois dans {ToolsFolder} (sur la clé USB, il sert ensuite sur tous les PC)");
            group.AddLink("Cinebench", "Page de téléchargement de Maxon", "https://www.maxon.net/en/downloads");
            progress("Charge du processeur pendant 10 minutes (sans Cinebench)…");
            sampler.Start();
            CpuLoad(TimeSpan.FromMinutes(10));
        }
        else
        {
            progress("Cinebench : test sur un cœur…");
            var single = Cinebench(cinebench, "g_CinebenchCpu1Test=true g_CinebenchMinimumTestDuration=1");
            progress("Cinebench : tous les cœurs pendant 10 minutes (charge et températures)…");
            sampler.Start();
            var multi = Cinebench(cinebench, "g_CinebenchCpuXTest=true g_CinebenchMinimumTestDuration=600");
            result.CinebenchMulti = multi?.ToString("N0", Fr);
            result.CinebenchSingle = single?.ToString("N0", Fr);
            group.Add("Version", FileVersionInfo.GetVersionInfo(cinebench).ProductVersion ?? Path.GetFileName(Path.GetDirectoryName(cinebench)));
            group.Add("Tous les cœurs", result.CinebenchMulti ?? "Pas de score (Cinebench n'a pas répondu)");
            group.Add("Un seul cœur", result.CinebenchSingle ?? "Pas de score");
            group.AddLink("Comparer", "Classement officiel Cinebench", "https://www.maxon.net/cinebench");
        }
        sampler.Stop();
        sampler.Report(load);
        section.Groups.Add(group);
    }

    private static double? Cinebench(string exe, string args)
    {
        // Maxon : « start /b /wait "parentconsole" » pour que Cinebench écrive son score dans la console.
        var lines = new List<string>();
        ProcessRunner.Run("cmd.exe", $"/c start \"parentconsole\" /b /wait \"{exe}\" {args}", l => lines.Add(l), TimeSpan.FromMinutes(25), Encoding.UTF8);
        var match = lines.SelectMany(l => Regex.Matches(l, @"\bCB\b\s*[:=]?\s*(\d+(?:[.,]\d+)?)").Cast<Match>()).LastOrDefault();
        return match is not null && double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Sans Cinebench : calculs sur tous les cœurs pour chauffer le processeur.</summary>
    private static void CpuLoad(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        Parallel.For(0, Environment.ProcessorCount, _ =>
        {
            var x = 1.0001;
            while (DateTime.UtcNow < end)
                for (var i = 0; i < 1_000_000; i++) x = Math.Sqrt(x * 1.000001 + i);
            GC.KeepAlive(x);
        });
    }

    /// <summary>Relève fréquence du processeur et températures toutes les 2 secondes pendant la charge.</summary>
    private sealed class Sampler : IDisposable
    {
        private readonly List<(DateTime Time, double? Perf, double? Acpi, double? Gpu)> _samples = new();
        private Timer? _timer;
        private PerformanceCounter? _perf;
        private int _tick;

        public void Start()
        {
            try { _perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); _perf.NextValue(); }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { _perf = null; }
            _timer = new Timer(_ => Sample(), null, 2000, 2000);
        }

        public void Stop() => _timer?.Dispose();

        private void Sample()
        {
            double? perf = null, acpi = null, gpu = null;
            try { perf = _perf?.NextValue(); } catch (InvalidOperationException) { }
            var zones = Wmi.QueryOptional("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", Wmi.WmiScope)
                .Select(z => z.ULong("CurrentTemperature")).Where(t => t is > 0).Select(t => t!.Value / 10.0 - 273.15).ToList();
            if (zones.Count > 0) acpi = zones.Max();
            if (_tick++ % 3 == 0) gpu = GpuSensors.MaxTemperature();
            lock (_samples) _samples.Add((DateTime.UtcNow, perf, acpi, gpu));
        }

        public void Report(InfoGroup group)
        {
            List<(DateTime Time, double? Perf, double? Acpi, double? Gpu)> samples;
            lock (_samples) samples = _samples.ToList();
            if (samples.Count < 10)
            {
                group.Add("Résultat", "Pas assez de mesures pendant la charge");
                return;
            }
            var start = samples[0].Time;
            var first = samples.Where(s => s.Time - start < TimeSpan.FromMinutes(1)).Select(s => s.Perf).Where(p => p is > 0).Select(p => p!.Value).ToList();
            var last = samples.Where(s => samples[samples.Count - 1].Time - s.Time < TimeSpan.FromMinutes(1)).Select(s => s.Perf).Where(p => p is > 0).Select(p => p!.Value).ToList();
            var verdict = "Stable";
            var level = "ok";
            if (first.Count > 0 && last.Count > 0)
            {
                var drop = 1 - last.Average() / first.Average();
                group.Add("Fréquence du processeur", $"{first.Average():0} % au début, {last.Average():0} % à la fin (100 % = fréquence de base)");
                if (drop > 0.25) { verdict = $"Baisse de fréquence de {drop:P0} : refroidissement à vérifier (pâte thermique, ventilateurs, poussière)"; level = "critical"; }
                else if (drop > 0.12) { verdict = $"Légère baisse de fréquence ({drop:P0}) : refroidissement un peu juste"; level = "warning"; }
            }
            var acpi = samples.Select(s => s.Acpi).Where(t => t is > 0 and < 130).Select(t => t!.Value).ToList();
            if (acpi.Count > 0)
            {
                var max = acpi.Max();
                group.AddBar("Température maxi (carte mère / processeur)", $"{max:0} °C", max / 105, max >= 95 ? "critical" : max >= 85 ? "warning" : "ok");
                if (max >= 95) { verdict = $"Surchauffe ({max:0} °C) : nettoyer et changer la pâte thermique"; level = "critical"; }
            }
            var gpu = samples.Select(s => s.Gpu).Where(t => t is > 0).Select(t => t!.Value).ToList();
            if (gpu.Count > 0)
            {
                var max = gpu.Max();
                group.AddBar("Température maxi (carte graphique)", $"{max:0} °C", max / 100, max >= 88 ? "critical" : max >= 80 ? "warning" : "ok");
            }
            if (acpi.Count == 0 && gpu.Count == 0)
                group.Add("Températures", "Pas de sonde lisible sans pilote : seule la fréquence est surveillée");
            group.Add("Verdict", level == "ok" ? "Stable : pas de baisse de fréquence sous charge" : verdict);
        }

        public void Dispose()
        {
            _timer?.Dispose();
            _perf?.Dispose();
        }
    }

    // ---------- Blender ----------

    private static double? BlenderBench(InfoSection section, BenchmarkResult result, Action<string> progress)
    {
        var group = new InfoGroup("Carte graphique (Blender Benchmark)");
        section.Groups.Add(group);
        var cli = FindTool("benchmark-launcher-cli.exe");
        if (cli is null)
        {
            group.Add("Résultat", $"Blender Benchmark absent : télécharger « benchmark-launcher-cli » sur opendata.blender.org et le mettre dans {ToolsFolder}");
            return null;
        }

        var workDir = Path.GetDirectoryName(cli)!;
        List<string> Cli(string args, TimeSpan timeout)
        {
            var lines = new List<string>();
            var start = new ProcessStartInfo(cli, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workDir,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            try
            {
                using var p = Process.Start(start);
                if (p is null) return lines;
                p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (lines) lines.Add(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit((int)timeout.TotalMilliseconds)) { try { p.Kill(); } catch (InvalidOperationException) { } }
                else p.WaitForExit();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            lock (lines) return lines.ToList();
        }

        var version = Cli("blender list", TimeSpan.FromMinutes(2))
            .Select(l => Regex.Match(l, @"\b(\d+\.\d+(?:\.\d+)?)\b")).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        if (version is null)
        {
            group.Add("Résultat", "Blender Benchmark ne répond pas (internet nécessaire la première fois)");
            return null;
        }
        string[] scenes = { "monster", "junkshop", "classroom" };
        progress($"Blender {version} : préparation (téléchargement la première fois, quelques centaines de Mo)…");
        Cli($"blender download {version}", TimeSpan.FromMinutes(30));
        Cli($"scenes --blender-version {version} download {string.Join(" ", scenes)}", TimeSpan.FromMinutes(30));

        var devices = Cli($"devices --blender-version {version}", TimeSpan.FromMinutes(3));
        var type = new[] { "OPTIX", "CUDA", "HIP", "ONEAPI", "METAL" }
            .FirstOrDefault(t => devices.Any(l => Regex.IsMatch(l, $@"\b{t}\b"))) ?? "CPU";
        var device = devices.FirstOrDefault(l => Regex.IsMatch(l, $@"\b{type}\b"))?.Trim();

        progress($"Blender : rendu des 3 scènes sur {(type == "CPU" ? "le processeur" : "la carte graphique")}…");
        var output = string.Join("\n", Cli($"benchmark --blender-version {version} --device-type {type} --json {string.Join(" ", scenes)}", TimeSpan.FromMinutes(40)));
        var values = Regex.Matches(output, @"""samples_per_minute""\s*:\s*([\d.]+)").Cast<Match>()
            .Select(m => double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToList();
        if (values.Count == 0)
        {
            group.Add("Résultat", "Pas de score (rendu impossible sur ce PC)");
            return null;
        }
        var score = values.Sum();
        result.BlenderScore = $"{score.ToString("N0", Fr)} ({(type == "CPU" ? "processeur" : type)})";
        group.Add("Version de Blender", version);
        group.Add("Calcul sur", type == "CPU" ? "Processeur (aucune carte graphique compatible détectée)" : $"{device} ({type})");
        group.Add("Score (échantillons/minute, 3 scènes)", score.ToString("N0", Fr));
        group.Add("Repères", "Carte d'entrée de gamme : environ 500 à 1 000 · RTX 3060 : environ 3 000 · RTX 4070 : environ 6 000 · RTX 4090 : 12 000 et plus (indicatif)");
        group.AddLink("Comparer", "Base de données Blender Open Data", "https://opendata.blender.org/");
        return type == "CPU" ? null : score;
    }

    private static string GamingVerdict(double? blender, WinSatScores winsat)
    {
        if (blender is { } b)
            return b switch
            {
                >= 9000 => "Haut de gamme : 1440p et 4K",
                >= 4500 => "Très bon : 1440p, ou 1080p en élevé",
                >= 2200 => "Bon : 1080p en réglages élevés",
                >= 900 => "Correct : 1080p en réglages moyens",
                _ => "Jeux légers (e-sport, jeux peu gourmands)",
            };
        return winsat.D3D switch
        {
            null => "Pas de mesure de la carte graphique",
            >= 9 => "Carte graphique dédiée : score à confirmer avec Blender Benchmark",
            _ => "Graphique intégré : jeux légers uniquement",
        };
    }

    private static string? FindTool(string exe)
    {
        try
        {
            return Directory.Exists(ToolsFolder)
                ? Directory.EnumerateFiles(ToolsFolder, exe, SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
