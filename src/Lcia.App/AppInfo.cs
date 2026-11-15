using System.Reflection;

namespace Lcia.App;

internal static class AppInfo
{
    public const string ProductName = "Local Code Intelligence Analyzer";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static string Title => $"{ProductName} {Version}";

    public static string InformationalVersion { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Version;
}
