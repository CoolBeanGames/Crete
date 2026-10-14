using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CreteLauncher.Core;

/// <summary>UI-agnostic launcher core: remote channels, download/install, launch, project scan.</summary>
public sealed class EngineService
{
    private readonly HttpClient _http;
    private readonly string _dataDir;

    public string DataDir => _dataDir;
    public string VersionsDir => Path.Combine(_dataDir, "versions");

    public EngineService(string? dataDir = null, HttpClient? http = null)
    {
        _dataDir = dataDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Crete", "Launcher");
        _http = http ?? new HttpClient();
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", "CreteLauncher");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        var token = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + token.Trim());
    }

    public string RepoFor(string channel) => channel.ToLowerInvariant() switch
    {
        "godot" => "godotengine/godot",
        "crete" => ResolveCreteRepo(),
        _ => throw new ArgumentException($"Unknown channel '{channel}'. Use 'godot' or 'crete'.", nameof(channel)),
    };

    private string ResolveCreteRepo()
    {
        var env = Environment.GetEnvironmentVariable("CRETE_REPO");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();
        var cfg = Path.Combine(_dataDir, "channels.json");
        if (File.Exists(cfg))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
            if (doc.RootElement.TryGetProperty("crete", out var el) && el.GetString() is { Length: > 0 } repo)
                return repo;
        }
        throw new ChannelNotConfiguredException(
            $"Crete channel has no release repo yet. Set CRETE_REPO=owner/repo or add {{\"crete\":\"owner/repo\"}} to {cfg}");
    }

    public async Task<IReadOnlyList<EngineRelease>> ListRemoteAsync(string channel, int max = 20, CancellationToken ct = default)
    {
        var repo = RepoFor(channel);
        using var resp = await _http.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=100", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var list = new List<EngineRelease>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var assets = new List<ReleaseAsset>();
            foreach (var a in el.GetProperty("assets").EnumerateArray())
            {
                assets.Add(new ReleaseAsset(
                    a.GetProperty("name").GetString()!,
                    a.GetProperty("browser_download_url").GetString()!,
                    a.GetProperty("size").GetInt64()));
            }
            list.Add(new EngineRelease(
                el.GetProperty("tag_name").GetString()!,
                el.GetProperty("name").GetString() ?? "",
                el.GetProperty("prerelease").GetBoolean(),
                el.GetProperty("draft").GetBoolean(),
                assets));
            if (list.Count >= max)
                break;
        }
        return list;
    }

    public IReadOnlyList<InstalledEngine> ListInstalled()
    {
        var outList = new List<InstalledEngine>();
        if (!Directory.Exists(VersionsDir))
            return outList;
        foreach (var manifest in Directory.EnumerateFiles(VersionsDir, "installed.json", SearchOption.AllDirectories))
        {
            try
            {
                var eng = JsonSerializer.Deserialize<InstalledEngine>(File.ReadAllText(manifest));
                if (eng is not null && File.Exists(eng.ExePath))
                    outList.Add(eng);
            }
            catch (Exception)
            {
                // Ignore corrupt/partial installs; reinstall overwrites them.
            }
        }
        return outList.OrderBy(e => e.Id).ToList();
    }

    public InstalledEngine RequireInstalled(string installId) =>
        ListInstalled().FirstOrDefault(e => e.Id.Equals(installId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"'{installId}' is not installed. Run 'list' to see installed versions.");

    public async Task<InstalledEngine> InstallAsync(
        string channel, string? tag, bool dotnet,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var releases = await ListRemoteAsync(channel, 100, ct);
        var rel = tag is null
            ? AssetPicker.PickLatestStable(releases, dotnet)
            : releases.FirstOrDefault(r => r.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (rel is null)
            throw new InvalidOperationException(tag is null
                ? $"Channel '{channel}' has no stable release with downloadable assets."
                : $"Channel '{channel}' has no release tagged '{tag}'.");
        var asset = AssetPicker.PickWindowsEditor(rel.Assets, dotnet)
            ?? throw new InvalidOperationException(
                $"Release {rel.Tag} has no Windows {(dotnet ? ".NET" : "standard")} editor archive.");

        var id = AssetPicker.InstallId(channel.ToLowerInvariant(), rel.Tag, dotnet);
        var dest = Path.Combine(VersionsDir, id);
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);

        var zipPath = Path.Combine(dest, asset.Name);
        using (var resp = await _http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = File.Create(zipPath);
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                got += n;
                progress?.Report(new DownloadProgress(total > 0 ? (double)got / total!.Value : 0, got, total));
            }
        }

        ZipFile.ExtractToDirectory(zipPath, dest);
        File.Delete(zipPath);
        var exe = FindEditorExe(dest)
            ?? throw new InvalidOperationException("Archive extracted but no editor .exe was found.");
        var installed = new InstalledEngine(id, channel.ToLowerInvariant(), rel.Tag, dotnet, exe, DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(dest, "installed.json"), JsonSerializer.Serialize(installed));
        return installed;
    }

    public static string? FindEditorExe(string dir) =>
        Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).Contains("console", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetFileName(p).StartsWith("Godot", StringComparison.OrdinalIgnoreCase) ||
                          Path.GetFileName(p).StartsWith("Crete", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Length)
            .FirstOrDefault();

    private static readonly HashSet<string> ImportSkipDirs =
        new(StringComparer.OrdinalIgnoreCase) { "obj", "build_deps", ".godot" };

    private static readonly HashSet<string> ImportSkipExts =
        new(StringComparer.OrdinalIgnoreCase) { ".pdb", ".exp", ".lib" };

    private static bool ImportSkipped(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => ImportSkipDirs.Contains(part)) ||
        ImportSkipExts.Contains(Path.GetExtension(relativePath));

    /// <summary>Register a locally built engine (e.g. engine/bin) as an install.
    /// Copies everything except build intermediates, so the install survives
    /// rebuilds and repo moves; re-import after rebuilding the engine.</summary>
    public async Task<InstalledEngine> ImportLocalAsync(
        string sourceDir, string? installId = null, string? tag = null,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(sourceDir))
            throw new ArgumentException($"Directory not found: {sourceDir}.", nameof(sourceDir));
        var srcExe = FindEditorExe(sourceDir)
            ?? throw new InvalidOperationException($"No editor .exe found under {sourceDir}.");
        var dotnet = Path.GetFileName(srcExe).Contains("mono", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(srcExe).Contains("dotnet", StringComparison.OrdinalIgnoreCase);

        tag ??= ReportVersion(srcExe);
        var id = string.IsNullOrWhiteSpace(installId)
            ? AssetPicker.InstallId("crete", "local", dotnet)
            : installId.Trim();
        var dest = Path.Combine(VersionsDir, id);
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);

        var files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(sourceDir, f))
            .Where(f => !ImportSkipped(f))
            .ToList();
        var total = files.Sum(f => new FileInfo(Path.Combine(sourceDir, f)).Length);
        long got = 0;
        foreach (var rel in files)
        {
            ct.ThrowIfCancellationRequested();
            var from = Path.Combine(sourceDir, rel);
            var to = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using (var src = File.OpenRead(from))
            using (var dst = File.Create(to))
            {
                await src.CopyToAsync(dst, ct);
            }
            got += new FileInfo(to).Length;
            progress?.Report(new DownloadProgress(total > 0 ? (double)got / total : 0, got, total));
        }

        var exe = FindEditorExe(dest)
            ?? throw new InvalidOperationException("Import copied no editor .exe.");
        var installed = new InstalledEngine(id, "crete", tag, dotnet, exe, DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(dest, "installed.json"), JsonSerializer.Serialize(installed));
        return installed;
    }

    private static string ReportVersion(string exe)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, "--headless --version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            });
            if (p is null)
                return "local";
            var first = p.StandardOutput.ReadLine();
            if (!p.WaitForExit(90000))
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { }
                return "local";
            }
            return string.IsNullOrWhiteSpace(first) ? "local" : first.Trim();
        }
        catch (Exception)
        {
            return "local";
        }
    }

    /// <summary>Run the installed editor binary with args and capture output (e.g. "--headless --version").</summary>
    public string RunEditorCapture(string installId, string args, int timeoutSeconds = 60)
    {
        var eng = RequireInstalled(installId);
        return Capture(eng.ExePath, args, Path.GetDirectoryName(eng.ExePath)!, timeoutSeconds);
    }

    /// <summary>Run any executable and capture combined output. Reads start before the
    /// wait: reading only after WaitForExit deadlocks against GUI-subsystem children
    /// like Godot that block while their pipes are unattended.</summary>
    public static string Capture(string exe, string args, string workingDirectory, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}.");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutSeconds * 1000))
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { }
            throw new TimeoutException($"'{exe} {args}' did not exit within {timeoutSeconds}s.");
        }
        return (outTask.GetAwaiter().GetResult() + errTask.GetAwaiter().GetResult()).Trim();
    }

    /// <summary>Prove an install is good: binary metadata plus a best-effort editor
    /// self-report. Plain --version inits the display server and hangs without an
    /// interactive desktop, so Godot 4+ is asked headless; older builds skip the run.</summary>
    public string Verify(string installId, int timeoutSeconds = 30)
    {
        var eng = RequireInstalled(installId);
        var fvi = FileVersionInfo.GetVersionInfo(eng.ExePath);
        var mib = new FileInfo(eng.ExePath).Length / 1048576;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{eng.Id}");
        sb.AppendLine($"  path: {eng.ExePath} ({mib} MiB)");
        sb.AppendLine($"  file version: {fvi.ProductVersion}");
        if (AssetPicker.ParseVersion(eng.Tag) is { Major: >= 4 })
        {
            try
            {
                sb.Append($"  editor reports: {RunEditorCapture(installId, "--headless --version", timeoutSeconds)}");
            }
            catch (Exception ex)
            {
                sb.Append($"  editor run skipped: {ex.Message}");
            }
        }
        else
        {
            sb.Append("  editor run skipped: headless self-report needs Godot 4+");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Open a project in the installed editor GUI (detached).</summary>
    public void Launch(string installId, string? projectPath = null)
    {
        var eng = RequireInstalled(installId);
        var args = projectPath is null ? "--editor" : $"--editor --path \"{projectPath}\"";
        Process.Start(new ProcessStartInfo(eng.ExePath, args) { UseShellExecute = true });
    }

    public IReadOnlyList<GameProject> FindProjects(string root, int maxDepth = 3)
    {
        var found = new List<GameProject>();
        if (!Directory.Exists(root))
            return found;
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = maxDepth,
            IgnoreInaccessible = true,
        };
        foreach (var f in Directory.EnumerateFiles(root, "project.godot", opts))
        {
            var dir = Path.GetDirectoryName(f)!;
            var csproj = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).Any();
            found.Add(new GameProject(new DirectoryInfo(dir).Name, dir, ReadVersionHint(f), csproj));
        }
        return found.OrderBy(p => p.Name).ToList();
    }

    public static string? ReadVersionHint(string projectGodotPath)
    {
        try
        {
            var m = Regex.Match(File.ReadAllText(projectGodotPath), @"""(\d+\.\d+[^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static readonly string[] Renderers = ["forward_plus", "mobile", "gl_compatibility"];

    /// <summary>Create a real Godot project on disk: folder + minimal project.godot.
    /// The engine fills in the rest (including the C# solution) on first open.</summary>
    public GameProject CreateProject(string name, string parentDir, string renderer = "forward_plus")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name is required.", nameof(name));
        if (!Renderers.Contains(renderer))
            throw new ArgumentException($"Renderer must be one of: {string.Join(", ", Renderers)}.", nameof(renderer));
        var dir = Path.GetFullPath(Path.Combine(parentDir, name.Trim()));
        if (File.Exists(Path.Combine(dir, "project.godot")))
            throw new InvalidOperationException($"'{dir}' is already a Godot project.");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "project.godot"),
            $"config_version=5\n\n[application]\n\nconfig/name=\"{name.Trim()}\"\n\n[rendering]\n\nrenderer/rendering_method=\"{renderer}\"\n");
        return new GameProject(name.Trim(), dir, null, false);
    }

    // ---------- launcher-local state: favorites + per-project engine ----------
    private string FavoritesPath => Path.Combine(_dataDir, "favorites.json");
    private string EnginesMapPath => Path.Combine(_dataDir, "project-engines.json");

    public HashSet<string> GetFavorites()
    {
        try
        {
            if (!File.Exists(FavoritesPath))
                return new(StringComparer.OrdinalIgnoreCase);
            return new(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesPath)) ?? [],
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <returns>True when the project is now a favorite.</returns>
    public bool ToggleFavorite(string projectPath)
    {
        var favs = GetFavorites();
        var on = favs.Add(projectPath);
        if (!on)
            favs.Remove(projectPath);
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(FavoritesPath, JsonSerializer.Serialize(favs.ToList()));
        return on;
    }

    public string? GetProjectEngine(string projectPath)
    {
        try
        {
            if (!File.Exists(EnginesMapPath))
                return null;
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(EnginesMapPath));
            if (raw is null)
                return null;
            var map = new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
            return map.TryGetValue(projectPath, out var id) ? id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void SetProjectEngine(string projectPath, string installId)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(EnginesMapPath))
                map = new(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(EnginesMapPath)) ?? [],
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Corrupt map: start over.
        }
        map[projectPath] = installId;
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(EnginesMapPath, JsonSerializer.Serialize(map));
    }
}
