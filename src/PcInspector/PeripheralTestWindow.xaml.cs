using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PcInspector;

/// <summary>Tests à faire avec le client : écran, clavier, haut-parleurs, micro, webcam. Rien n'est modifié ni enregistré.</summary>
public partial class PeripheralTestWindow : Window
{
    // Disposition AZERTY : les codes de touche Windows suivent la position des lettres imprimées.
    private static readonly Key[][] Rows =
    {
        new[] { Key.Escape, Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12, Key.Delete },
        new[] { Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0, Key.Back },
        new[] { Key.Tab, Key.A, Key.Z, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P, Key.Return },
        new[] { Key.CapsLock, Key.Q, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L, Key.M },
        new[] { Key.LeftShift, Key.W, Key.X, Key.C, Key.V, Key.B, Key.N, Key.RightShift, Key.Up },
        new[] { Key.LeftCtrl, Key.LWin, Key.LeftAlt, Key.Space, Key.RightAlt, Key.RightCtrl, Key.Left, Key.Down, Key.Right },
    };

    private readonly Dictionary<Key, Border> _keys = new();
    private readonly SortedSet<string> _others = new();
    private bool _keyboardTest;

    public PeripheralTestWindow()
    {
        InitializeComponent();
        foreach (var row in Rows)
        {
            var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var key in row)
            {
                var border = new Border
                {
                    MinWidth = key == Key.Space ? 180 : 40,
                    Height = 34,
                    Margin = new Thickness(0, 0, 4, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = (Brush)FindResource("ContentBrush"),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock { Text = Label(key), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) },
                };
                _keys[key] = border;
                panel.Children.Add(border);
            }
            KeyRows.Children.Add(panel);
        }
    }

    private static string Label(Key key) => key switch
    {
        Key.Escape => "Échap",
        Key.Delete => "Suppr",
        Key.Back => "⟵",
        Key.Tab => "Tab",
        Key.Return => "Entrée",
        Key.CapsLock => "Verr. maj",
        Key.LeftShift or Key.RightShift => "Maj",
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LWin => "Win",
        Key.LeftAlt => "Alt",
        Key.RightAlt => "Alt Gr",
        Key.Space => "Espace",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };

    // ---------- Écran ----------

    private void ScreenTest_Click(object sender, RoutedEventArgs e)
    {
        var colors = new[] { Colors.White, Colors.Black, Colors.Red, Colors.Lime, Colors.Blue, Color.FromRgb(128, 128, 128) };
        var index = 0;
        var screen = new Window
        {
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Background = new SolidColorBrush(colors[0]),
            Cursor = Cursors.None,
            Owner = this,
        };
        void Next()
        {
            index++;
            if (index >= colors.Length) screen.Close();
            else screen.Background = new SolidColorBrush(colors[index]);
        }
        screen.MouseLeftButtonUp += (_, _) => Next();
        screen.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) screen.Close();
            else if (args.Key is Key.Space or Key.Right or Key.Return) Next();
        };
        screen.ShowDialog();
    }

    // ---------- Clavier ----------

    private void KeyboardButton_Click(object sender, RoutedEventArgs e)
    {
        _keyboardTest = !_keyboardTest;
        KeyboardButton.Content = _keyboardTest ? "Arrêter le test du clavier" : "Démarrer le test du clavier";
        KeyboardHint.Text = _keyboardTest
            ? "Test en cours : appuyer sur chaque touche. Les touches de raccourci (Échap, Tab, Alt…) ne déclenchent rien pendant le test."
            : "Démarrer le test, puis appuyer sur chaque touche : elle passe au vert. Les touches restées grises n'ont pas répondu.";
        if (_keyboardTest)
        {
            foreach (var border in _keys.Values) border.Background = (Brush)FindResource("ContentBrush");
            _others.Clear();
            OtherKeys.Text = "";
            // Le bouton garderait le focus et Espace le recliquerait.
            Keyboard.ClearFocus();
            Focus();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_keyboardTest) return;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        // Alt Gr arrive comme Ctrl gauche + Alt droite.
        if (_keys.TryGetValue(key, out var border)) border.Background = (Brush)FindResource("OkBrush");
        else
        {
            _others.Add(key.ToString());
            OtherKeys.Text = "Autres touches qui répondent : " + string.Join(", ", _others);
        }
        e.Handled = true;
    }

    // ---------- Haut-parleurs ----------

    private SoundPlayer? _player;

    private void Speaker_Click(object sender, RoutedEventArgs e)
    {
        var side = (sender as FrameworkElement)?.Tag as string;
        // Gardé en champ : le son est joué en arrière-plan et ne doit pas être libéré avant la fin.
        _player?.Stop();
        _player = new SoundPlayer(Tone(side != "right", side != "left"));
        try { _player.Play(); }
        catch (InvalidOperationException ex) { MessageBox.Show(this, "Lecture impossible : " + ex.Message, Title); }
    }

    /// <summary>Un bip de 440 Hz d'une seconde, en WAV stéréo, sur le côté demandé.</summary>
    private static MemoryStream Tone(bool left, bool right)
    {
        const int rate = 44100, seconds = 1;
        var samples = rate * seconds;
        var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write("RIFF".ToCharArray()); w.Write(36 + samples * 4); w.Write("WAVE".ToCharArray());
        w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data".ToCharArray()); w.Write(samples * 4);
        for (var i = 0; i < samples; i++)
        {
            // Montée et descente douces pour éviter le « clic ».
            var envelope = Math.Min(1.0, Math.Min(i, samples - i) / (rate * 0.02));
            var value = (short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 12000 * envelope);
            w.Write(left ? value : (short)0);
            w.Write(right ? value : (short)0);
        }
        w.Flush();
        stream.Position = 0;
        return stream;
    }

    // ---------- Micro ----------

    private MicMeter? _mic;
    private DispatcherTimer? _micTimer;

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mic is not null)
        {
            StopMic();
            return;
        }
        try
        {
            _mic = new MicMeter();
            _micTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _micTimer.Tick += (_, _) =>
            {
                if (_mic is null) return;
                var level = _mic.ReadLevel();
                if (level is { } l) MicLevel.Value = l;
            };
            _micTimer.Start();
            MicButton.Content = "Arrêter";
            MicStatus.Text = "Écoute en cours sur le micro par défaut de Windows.";
        }
        catch (InvalidOperationException ex)
        {
            _mic = null;
            MicStatus.Text = ex.Message;
        }
    }

    private void StopMic()
    {
        _micTimer?.Stop();
        _micTimer = null;
        _mic?.Dispose();
        _mic = null;
        MicLevel.Value = 0;
        MicButton.Content = "Écouter le micro";
        MicStatus.Text = "";
    }

    // ---------- Webcam ----------

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string target) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Win32Exception ex) { MessageBox.Show(this, "Ouverture impossible : " + ex.Message, Title); }
    }

    private void Window_Closed(object? sender, EventArgs e) => StopMic();

    /// <summary>
    /// Niveau du micro par défaut, lu avec l'API audio de base de Windows (waveIn) : le son est analysé puis oublié.
    /// </summary>
    private sealed class MicMeter : IDisposable
    {
        private const int BufferBytes = 3200; // 100 ms en 16 kHz mono 16 bits
        private const int WaveMapper = -1;
        private const int WhdrDone = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormat
        {
            public ushort FormatTag, Channels;
            public uint SamplesPerSec, AvgBytesPerSec;
            public ushort BlockAlign, BitsPerSample, Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHeader
        {
            public IntPtr Data;
            public uint BufferLength, BytesRecorded;
            public IntPtr User;
            public uint Flags, Loops;
            public IntPtr Next, Reserved;
        }

        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr handle, int device, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr handle);

        private static readonly int HeaderSize = Marshal.SizeOf<WaveHeader>();
        private static readonly int FlagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
        private static readonly int RecordedOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.BytesRecorded));

        private readonly IntPtr _handle;
        private readonly List<(IntPtr Header, IntPtr Data)> _buffers = new();
        private readonly byte[] _samples = new byte[BufferBytes];
        private double _level;

        public MicMeter()
        {
            var format = new WaveFormat { FormatTag = 1, Channels = 1, SamplesPerSec = 16000, AvgBytesPerSec = 32000, BlockAlign = 2, BitsPerSample = 16 };
            if (waveInOpen(out _handle, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                throw new InvalidOperationException("Aucun micro disponible, ou accès au micro coupé dans Paramètres > Confidentialité > Microphone.");

            for (var i = 0; i < 3; i++)
            {
                var data = Marshal.AllocHGlobal(BufferBytes);
                var header = Marshal.AllocHGlobal(HeaderSize);
                Marshal.StructureToPtr(new WaveHeader { Data = data, BufferLength = BufferBytes }, header, false);
                waveInPrepareHeader(_handle, header, HeaderSize);
                waveInAddBuffer(_handle, header, HeaderSize);
                _buffers.Add((header, data));
            }
            waveInStart(_handle);
        }

        /// <summary>Niveau de 0 à 100 des derniers morceaux de son reçus, ou null si rien de neuf.</summary>
        public double? ReadLevel()
        {
            double? peak = null;
            foreach (var (header, data) in _buffers)
            {
                if ((Marshal.ReadInt32(header, FlagsOffset) & WhdrDone) == 0) continue;
                var recorded = Math.Min(BufferBytes, Marshal.ReadInt32(header, RecordedOffset));
                Marshal.Copy(data, _samples, 0, recorded);
                var max = 0;
                for (var i = 0; i + 1 < recorded; i += 2) max = Math.Max(max, Math.Abs((int)BitConverter.ToInt16(_samples, i)));
                // Échelle logarithmique : une voix normale remplit la moitié de la barre.
                var value = max <= 0 ? 0 : Math.Max(0, Math.Min(100, (20 * Math.Log10(max / 32768.0) + 60) * 100 / 60));
                peak = Math.Max(peak ?? 0, value);
                waveInAddBuffer(_handle, header, HeaderSize);
            }
            // Retombée progressive de la barre.
            if (peak is { } p) _level = Math.Max(p, _level * 0.7);
            return peak is null ? null : _level;
        }

        public void Dispose()
        {
            waveInReset(_handle);
            foreach (var (header, data) in _buffers)
            {
                waveInUnprepareHeader(_handle, header, HeaderSize);
                Marshal.FreeHGlobal(header);
                Marshal.FreeHGlobal(data);
            }
            _buffers.Clear();
            waveInClose(_handle);
        }
    }
}
