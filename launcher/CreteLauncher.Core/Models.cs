namespace CreteLauncher.Core;

public sealed record ReleaseAsset(string Name, string BrowserDownloadUrl, long Size);

public sealed record EngineRelease(string Tag, string Name, bool Prerelease, bool Draft, IReadOnlyList<ReleaseAsset> Assets);

public sealed record InstalledEngine(string Id, string Channel, string Tag, bool Dotnet, string ExePath, DateTimeOffset InstalledAt);

public sealed record GameProject(string Name, string Path, string? VersionHint, bool HasCsproj);

public sealed record DownloadProgress(double Fraction, long BytesReceived, long? TotalBytes);

public sealed class ChannelNotConfiguredException(string message) : Exception(message);
