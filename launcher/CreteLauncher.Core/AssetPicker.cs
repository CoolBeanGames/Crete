namespace CreteLauncher.Core;

/// <summary>Pure selection rules for release assets. No I/O: covered by unit tests.</summary>
public static class AssetPicker
{
    /// <summary>Pick the Windows editor archive for a release. Standard builds ship as
    /// <c>*_win64.exe.zip</c>; .NET builds ship as <c>*_win64_dotnet.zip</c> (older: <c>_mono_</c>).</summary>
    public static ReleaseAsset? PickWindowsEditor(IEnumerable<ReleaseAsset> assets, bool dotnet)
    {
        var list = assets.ToList();
        if (dotnet)
        {
            return list.FirstOrDefault(a =>
                a.Name.Contains("win64", StringComparison.OrdinalIgnoreCase) &&
                (a.Name.Contains("dotnet", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Contains("mono", StringComparison.OrdinalIgnoreCase)) &&
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }
        return list.FirstOrDefault(a =>
            a.Name.Contains("win64", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".exe.zip", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsStableRelease(EngineRelease r) =>
        !r.Prerelease && !r.Draft && r.Tag.EndsWith("-stable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Parse "4.7.2-stable" to a comparable version. Null when the tag has no numeric prefix.</summary>
    public static Version? ParseVersion(string tag)
    {
        var m = System.Text.RegularExpressions.Regex.Match(tag, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success)
            return null;
        return new Version(
            int.Parse(m.Groups[1].Value),
            int.Parse(m.Groups[2].Value),
            m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    /// <summary>Highest-version stable release that actually has a matching Windows editor asset.
    /// API date order is wrong: Godot backports 3.x maintenance releases after 4.x majors.</summary>
    public static EngineRelease? PickLatestStable(IEnumerable<EngineRelease> releases, bool dotnet) =>
        releases
            .Where(r => IsStableRelease(r) && PickWindowsEditor(r.Assets, dotnet) is not null)
            .Select(r => (Release: r, Version: ParseVersion(r.Tag)))
            .Where(t => t.Version is not null)
            .OrderByDescending(t => t.Version)
            .Select(t => t.Release)
            .FirstOrDefault();

    public static string DisplayVersion(string tag) =>
        tag.EndsWith("-stable", StringComparison.OrdinalIgnoreCase) ? tag[..^"-stable".Length] : tag;

    public static string InstallId(string channel, string tag, bool dotnet) =>
        $"{channel}-{tag}" + (dotnet ? "-dotnet" : "");
}
