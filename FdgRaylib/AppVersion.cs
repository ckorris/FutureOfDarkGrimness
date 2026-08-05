using System.Reflection;

namespace FdgRaylib;

/// <summary>
/// The build stamp bug reports carry (#226) - without it a report can't be tied to the binary
/// that produced it. Dist builds are stamped by <c>scripts/build-dist.sh</c> via
/// <c>-p:InformationalVersion=git-&lt;short-sha&gt;-&lt;utc-date&gt;</c>; anything else
/// (<c>dotnet run</c>, IDE builds) reports the csproj default "dev". The SDK may append
/// "+&lt;commit&gt;" on its own when building inside a git checkout - extra precision, kept.
/// </summary>
public static class AppVersion
{
    public static string Value { get; } =
        typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        is { Length: > 0 } info ? info : "unknown";
}
