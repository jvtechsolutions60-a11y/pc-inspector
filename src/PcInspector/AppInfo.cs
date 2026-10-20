using System.Reflection;

namespace PcInspector;

/// <summary>Nom et version du logiciel lancé (v1.0.N, N = numéro de compilation GitHub Actions), partagés par les trois logiciels.</summary>
public static class AppInfo
{
    private static Assembly Entry => Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;

    public static string Version
    {
        get
        {
            var v = Entry.GetName().Version;
            return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static string Product => Entry.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "PC Inspector";
}
