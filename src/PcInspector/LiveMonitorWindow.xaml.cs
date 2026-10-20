using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PcInspector.Collectors;

namespace PcInspector;

/// <summary>Courbes de la charge processeur, de la mémoire et des températures lisibles sans pilote. Lecture seule.</summary>
public partial class LiveMonitorWindow : Window
{
    private const int Points = 150; // 5 minutes à raison d'un point toutes les 2 secondes
    private const double ChartHeight = 90;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, Chart> _charts = new();
    private (ulong Idle, ulong Total)? _lastCpu;
    private int _tick;
    private bool _busy;
    private Dictionary<string, double> _diskTemps = new();

    public LiveMonitorWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await SampleAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        GetChart("Processeur", "%", 100, 80, 95);
        GetChart("Mémoire utilisée", "%", 100, 85, 95);
        await SampleAsync();
        _timer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e) => _timer.Stop();

    private async Task SampleAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            GetChart("Processeur", "%", 100, 80, 95).Add(CpuPercent());
            GetChart("Mémoire utilisée", "%", 100, 85, 95).Add(MemoryPercent());

            var zones = await Task.Run(ThermalZones);
            foreach (var zone in zones) GetChart("Température " + zone.Key, "°C", 110, 80, 95).Add(zone.Value);

            // Les températures des disques sont plus lentes à lire : une fois sur cinq suffit.
            if (_tick++ % 5 == 0) _diskTemps = await Task.Run(DiskTemperatures);
            foreach (var disk in _diskTemps) GetChart("Température " + disk.Key, "°C", 80, 55, 65).Add(disk.Value);

            if (zones.Count == 0 && _diskTemps.Count == 0 && !_charts.ContainsKey("info"))
            {
                _charts["info"] = null!;
                Charts.Children.Add(new TextBlock
                {
                    Text = "Ce PC n'expose aucune sonde de température lisible sans pilote : seules la charge et la mémoire sont suivies.",
                    Foreground = (Brush)FindResource("MutedTextBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private Chart GetChart(string title, string unit, double max, double warn, double danger)
    {
        if (_charts.TryGetValue(title, out var chart) && chart is not null) return chart;
        chart = new Chart(title, unit, max, warn, danger, this);
        _charts[title] = chart;
        Charts.Children.Add(chart.Root);
        return chart;
    }

    // ---------- Mesures ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private double CpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        // Le temps « noyau » inclut déjà le temps d'inactivité.
        var now = (Idle: idle.Value, Total: kernel.Value + user.Value);
        var last = _lastCpu;
        _lastCpu = now;
        if (last is not { } previous || now.Total <= previous.Total) return 0;
        var busy = 1.0 - (double)(now.Idle - previous.Idle) / (now.Total - previous.Total);
        return Math.Max(0, Math.Min(100, busy * 100));
    }

    private static double MemoryPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : 0;
    }

    private static Dictionary<string, double> ThermalZones()
    {
        var result = new Dictionary<string, double>();
        foreach (var zone in Wmi.QueryOptional("SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", Wmi.WmiScope))
        {
            if (zone.ULong("CurrentTemperature") is not { } tenths || tenths == 0) continue;
            var name = zone.Str("InstanceName")?.Split('\\').LastOrDefault() ?? "zone";
            result["zone " + name] = tenths / 10.0 - 273.15;
        }
        return result;
    }

    private static Dictionary<string, double> DiskTemperatures()
    {
        var names = Wmi.QueryOptional("SELECT DeviceId, FriendlyName FROM MSFT_PhysicalDisk", Wmi.StorageScope)
            .Where(d => d.Str("DeviceId") is not null)
            .GroupBy(d => d.Str("DeviceId")!)
            .ToDictionary(g => g.Key, g => g.First().Str("FriendlyName"));
        var result = new Dictionary<string, double>();
        foreach (var counter in Wmi.QueryOptional("SELECT DeviceId, Temperature FROM MSFT_StorageReliabilityCounter", Wmi.StorageScope))
        {
            if (counter.ULong("Temperature") is not { } t || t == 0) continue;
            var id = counter.Str("DeviceId") ?? "";
            result[names.TryGetValue(id, out var n) && n is not null ? n : "disque " + id] = t;
        }
        return result;
    }

    // ---------- Courbe ----------

    private sealed class Chart
    {
        private readonly List<double> _values = new();
        private readonly string _unit;
        private readonly double _max, _warn, _danger;
        private readonly TextBlock _current = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
        private readonly TextBlock _range = new();
        private readonly Polyline _line = new() { StrokeThickness = 2 };
        private readonly Canvas _canvas = new() { Height = ChartHeight, ClipToBounds = true };
        private readonly Window _owner;

        public Chart(string title, string unit, double max, double warn, double danger, Window owner)
        {
            _unit = unit;
            _max = max;
            _warn = warn;
            _danger = danger;
            _owner = owner;
            _range.Foreground = (Brush)owner.FindResource("MutedTextBrush");
            _current.Foreground = (Brush)owner.FindResource("TextBrush");
            _line.Stroke = (Brush)owner.FindResource("AccentBrush");

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(_current, Dock.Right);
            header.Children.Add(_current);
            var titles = new StackPanel();
            titles.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = (Brush)owner.FindResource("TextBrush") });
            titles.Children.Add(_range);
            header.Children.Add(titles);

            // Ligne d'alerte : au-dessus, c'est trop chaud ou trop chargé.
            var limit = new Line { Stroke = (Brush)owner.FindResource("WarningBrush"), StrokeDashArray = new DoubleCollection { 4, 3 }, StrokeThickness = 1 };
            _canvas.Children.Add(limit);
            _canvas.Children.Add(_line);
            _canvas.SizeChanged += (_, _) =>
            {
                limit.X1 = 0;
                limit.X2 = _canvas.ActualWidth;
                limit.Y1 = limit.Y2 = Y(_warn);
                Redraw();
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(new Border { Child = _canvas, Background = (Brush)owner.FindResource("ContentBrush"), CornerRadius = new CornerRadius(4) });
            Root = new Border
            {
                Child = body,
                Background = (Brush)owner.FindResource("CardBrush"),
                BorderBrush = (Brush)owner.FindResource("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12),
            };
        }

        public Border Root { get; }

        public void Add(double value)
        {
            _values.Add(value);
            if (_values.Count > Points) _values.RemoveAt(0);
            _current.Text = $"{value:0} {_unit}";
            _current.Foreground = (Brush)_owner.FindResource(value >= _danger ? "CriticalBrush" : value >= _warn ? "WarningBrush" : "TextBrush");
            _range.Text = $"min {_values.Min():0} {_unit} · max {_values.Max():0} {_unit}";
            Redraw();
        }

        private double Y(double value) => ChartHeight - Math.Max(0, Math.Min(_max, value)) / _max * ChartHeight;

        private void Redraw()
        {
            var width = _canvas.ActualWidth;
            if (width <= 0) return;
            var step = width / (Points - 1);
            var start = Points - _values.Count;
            _line.Points = new PointCollection(_values.Select((v, i) => new Point((start + i) * step, Y(v))));
        }
    }
}
