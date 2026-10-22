using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Threading;

namespace JvtechServeur.Pxe;

/// <summary>Un PC en cours de préparation, vu par le serveur.</summary>
internal sealed class Poste : INotifyPropertyChanged
{
    private string _model = "PC inconnu", _serial = "", _step = "", _detail = "", _state = "En cours";
    private DateTime _updated = DateTime.Now;

    public HashSet<string> Macs { get; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime Started { get; set; } = DateTime.Now;
    public DateTime? Finished { get; set; }
    public string? ReportPath { get; set; }

    public string Model { get => _model; set => Set(ref _model, value); }
    public string Serial { get => _serial; set => Set(ref _serial, value); }
    public string Step { get => _step; set => Set(ref _step, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    /// <summary>En cours, Terminé, À vérifier ou Sans nouvelles.</summary>
    public string State { get => _state; set => Set(ref _state, value); }
    public DateTime Updated { get => _updated; set { Set(ref _updated, value); OnChanged(nameof(Duration)); } }

    public string Title => Serial.Length > 0 ? $"{Model} · {Serial}" : $"{Model} · {Macs.FirstOrDefault()}";
    public string Duration
    {
        get
        {
            var span = (Finished ?? DateTime.Now) - Started;
            return span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00}" : $"{(int)span.TotalMinutes} min";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        OnChanged(name);
        OnChanged(nameof(Title));
    }

    public void Refresh() => OnChanged(nameof(Duration));

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Les postes suivis à l'écran, et l'historique des PC préparés (historique.csv) pour les statistiques.</summary>
internal sealed class Postes
{
    private readonly Dispatcher _dispatcher;

    public ObservableCollection<Poste> Items { get; } = new();

    public Postes(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>Un PC vient de recevoir l'ordre de démarrer sur le serveur.</summary>
    public void Booting(string mac) => _dispatcher.InvokeAsync(() =>
    {
        var poste = Find(new[] { mac }, "");
        if (poste is null || poste.Finished is not null)
        {
            poste = new Poste();
            poste.Macs.Add(mac);
            Items.Insert(0, poste);
        }
        poste.Started = DateTime.Now;
        poste.Step = "Démarrage réseau";
        poste.Detail = "Chargement de Windows PE puis effacement du disque et installation de Windows 11";
        poste.State = "En cours";
        poste.Updated = DateTime.Now;
    });

    /// <summary>Message envoyé par PC Maintenance pendant la préparation (lignes « Clé=valeur »).</summary>
    public void Status(Dictionary<string, string> values) => _dispatcher.InvokeAsync(() =>
    {
        var macs = values.TryGetValue("Macs", out var m) ? m.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().Replace('-', ':')).ToList() : new List<string>();
        var serial = values.TryGetValue("Serie", out var s) ? s : "";
        var poste = Find(macs, serial);
        if (poste is null)
        {
            poste = new Poste();
            Items.Insert(0, poste);
            if (values.TryGetValue("Debut", out var d) && DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)) poste.Started = start;
        }
        foreach (var mac in macs) poste.Macs.Add(mac);
        if (serial.Length > 0) poste.Serial = serial;
        if (values.TryGetValue("Modele", out var model) && model.Length > 0) poste.Model = model;
        if (values.TryGetValue("Etape", out var step)) poste.Step = step;
        if (values.TryGetValue("Detail", out var detail)) poste.Detail = detail;
        poste.Updated = DateTime.Now;
        if (values.TryGetValue("Fini", out var done) && done == "1")
        {
            poste.Finished = DateTime.Now;
            poste.State = values.TryGetValue("AVerifier", out var check) && check != "0" ? "À vérifier" : "Terminé";
            AppendHistory(poste);
        }
        else poste.State = "En cours";
    });

    public void Report(string serial, string path) => _dispatcher.InvokeAsync(() =>
    {
        var poste = Items.FirstOrDefault(p => p.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase));
        if (poste is not null) poste.ReportPath = path;
    });

    /// <summary>Toutes les minutes : durées à jour, et « sans nouvelles » au bout de 3 heures sans message.</summary>
    public void Tick()
    {
        foreach (var poste in Items)
        {
            poste.Refresh();
            if (poste.Finished is null && poste.State == "En cours" && DateTime.Now - poste.Updated > TimeSpan.FromHours(3))
                poste.State = "Sans nouvelles";
        }
    }

    public void ClearFinished()
    {
        foreach (var poste in Items.Where(p => p.Finished is not null).ToList()) Items.Remove(poste);
    }

    private Poste? Find(IEnumerable<string> macs, string serial) =>
        Items.FirstOrDefault(p => (serial.Length > 0 && p.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase)) || macs.Any(p.Macs.Contains));

    // ---------- Historique et statistiques ----------

    private static void AppendHistory(Poste poste)
    {
        try
        {
            Directory.CreateDirectory(ServerConfig.Root);
            if (!File.Exists(ServerConfig.HistoryPath))
                File.WriteAllText(ServerConfig.HistoryPath, "Debut;Fin;Modele;Serie;Minutes;Resultat\r\n", new UTF8Encoding(true));
            static string Clean(string text) => text.Replace(';', ',').Replace("\r", " ").Replace("\n", " ");
            var minutes = (int)((poste.Finished ?? DateTime.Now) - poste.Started).TotalMinutes;
            File.AppendAllText(ServerConfig.HistoryPath,
                $"{poste.Started:yyyy-MM-dd HH:mm};{poste.Finished:yyyy-MM-dd HH:mm};{Clean(poste.Model)};{Clean(poste.Serial)};{minutes};{Clean(poste.State)}\r\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal sealed record Stats(int ThisMonth, int LastMonth, int AverageMinutes, int ToCheck, IReadOnlyList<(string Model, int Count)> TopModels);

    public static Stats ReadStats()
    {
        var rows = new List<(DateTime Start, string Model, int Minutes, string Result)>();
        try
        {
            if (File.Exists(ServerConfig.HistoryPath))
            {
                foreach (var line in File.ReadAllLines(ServerConfig.HistoryPath).Skip(1))
                {
                    var p = line.Split(';');
                    if (p.Length < 6 || !DateTime.TryParse(p[0], out var start)) continue;
                    rows.Add((start, p[2], int.TryParse(p[4], out var min) ? min : 0, p[5]));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var current = rows.Where(r => r.Start >= month).ToList();
        var previous = rows.Count(r => r.Start >= month.AddMonths(-1) && r.Start < month);
        var average = current.Count == 0 ? 0 : (int)current.Average(r => r.Minutes);
        var top = current.GroupBy(r => r.Model).Select(g => (g.Key, g.Count())).OrderByDescending(g => g.Item2).Take(3).ToList();
        return new Stats(current.Count, previous, average, current.Count(r => r.Result != "Terminé"), top);
    }
}
