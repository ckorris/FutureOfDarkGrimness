using System.Reflection;

namespace FdgRaylib;

/// <summary>
/// The build stamp bug reports carry (#226) - without it a report can't be tied to the binary
/// that produced it. Dist/CI builds are stamped via
/// <c>-p:InformationalVersion=&lt;version&gt;+git-&lt;short-sha&gt;-&lt;utc-date&gt;</c>; anything else
/// (<c>dotnet run</c>, IDE builds) reports the Directory.Build.props default
/// "&lt;version&gt;-dev". The SDK may append "+&lt;commit&gt;" on its own when building inside
/// a git checkout - extra precision, kept.
/// </summary>
public static class AppVersion
{
    public static string Value { get; } =
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        is { Length: > 0 } info ? info : "unknown";
}
