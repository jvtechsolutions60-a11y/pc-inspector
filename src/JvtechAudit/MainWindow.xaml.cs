using System.Reflection;
using System.Windows;

namespace JvtechAudit;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var version = v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        Title = "JVTECH Audit " + version;
        VersionText.Text = $"{version} · Lecture seule : rien n'est modifié sur ce PC";
    }
}
