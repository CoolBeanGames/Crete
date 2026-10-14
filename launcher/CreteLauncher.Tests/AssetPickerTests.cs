using CreteLauncher.Core;

namespace CreteLauncher.Tests;

public sealed class AssetPickerTests
{
    private static ReleaseAsset A(string name) => new(name, "https://example.invalid/" + name, 1);

    private static readonly string[] Godot43Assets =
    [
        "Godot_v4.3-stable_android_editor.apk",
        "Godot_v4.3-stable_dotnet_android_editor.apk",
        "Godot_v4.3-stable_export_templates.tpz",
        "Godot_v4.3-stable_linux.x86_64.zip",
        "Godot_v4.3-stable_macos.universal.zip",
        "Godot_v4.3-stable_mono_win64.zip",
        "Godot_v4.3-stable_win32.exe.zip",
        "Godot_v4.3-stable_win64.exe.zip",
    ];

    [Fact]
    public void Picks_Standard_Win64_Editor()
    {
        var got = AssetPicker.PickWindowsEditor(Godot43Assets.Select(A), dotnet: false);
        Assert.Equal("Godot_v4.3-stable_win64.exe.zip", got?.Name);
    }

    [Fact]
    public void Picks_Old_Mono_Win64_For_Dotnet()
    {
        var got = AssetPicker.PickWindowsEditor(Godot43Assets.Select(A), dotnet: true);
        Assert.Equal("Godot_v4.3-stable_mono_win64.zip", got?.Name);
    }

    [Fact]
    public void Picks_New_Dotnet_Win64_For_Dotnet()
    {
        var assets = Godot43Assets.Where(n => !n.Contains("mono")).Select(A).Append(A("Godot_v4.5-stable_dotnet_win64.zip"));
        var got = AssetPicker.PickWindowsEditor(assets, dotnet: true);
        Assert.Equal("Godot_v4.5-stable_dotnet_win64.zip", got?.Name);
    }

    [Fact]
    public void Returns_Null_When_No_Windows_Editor()
    {
        var assets = new[] { A("Godot_v4.3-stable_linux.x86_64.zip"), A("Godot_v4.3-stable_export_templates.tpz") };
        Assert.Null(AssetPicker.PickWindowsEditor(assets, dotnet: false));
        Assert.Null(AssetPicker.PickWindowsEditor(assets, dotnet: true));
    }

    [Fact]
    public void Never_Picks_Templates_As_Editor()
    {
        var assets = new[] { A("Godot_v4.3-stable_win64.exe.zip"), A("Godot_v4.3-stable_export_templates.tpz") };
        Assert.Equal("Godot_v4.3-stable_win64.exe.zip", AssetPicker.PickWindowsEditor(assets, false)?.Name);
    }

    [Theory]
    [InlineData("4.3-stable", true)]
    [InlineData("4.4-beta1", false)]
    [InlineData("3.5.3-stable", true)]
    public void Stable_Detection(string tag, bool stable)
    {
        var r = new EngineRelease(tag, tag, Prerelease: false, Draft: false, []);
        Assert.Equal(stable, AssetPicker.IsStableRelease(r));
    }

    [Fact]
    public void Prerelease_Flag_Beats_Stable_Name()
    {
        var r = new EngineRelease("4.4-stable", "x", Prerelease: true, Draft: false, []);
        Assert.False(AssetPicker.IsStableRelease(r));
    }

    [Theory]
    [InlineData("4.3-stable", "4.3")]
    [InlineData("4.4-beta1", "4.4-beta1")]
    public void Display_Version_Strips_Stable(string tag, string want)
    {
        Assert.Equal(want, AssetPicker.DisplayVersion(tag));
    }

    [Fact]
    public void Latest_Stable_Prefers_Highest_Version_Not_Newest_Date()
    {
        static EngineRelease R(string tag, bool pre = false, bool winAsset = true) => new(
            tag, tag, Prerelease: pre, Draft: false,
            winAsset ? [A($"Godot_v{tag}_win64.exe.zip")] : [A($"Godot_v{tag}_linux.x86_64.zip")]);
        var rels = new[]
        {
            R("3.6.3-stable"),          // newest by date, lower version
            R("4.7.2-stable"),          // highest version -> winner
            R("4.8-beta1", pre: true),  // prerelease excluded
            R("4.7.3-stable", winAsset: false), // no windows asset, excluded
        };
        Assert.Equal("4.7.2-stable", AssetPicker.PickLatestStable(rels, dotnet: false)?.Tag);
    }

    [Theory]
    [InlineData("4.7.2-stable", "4.7.2")]
    [InlineData("4.3-stable", "4.3.0")]
    [InlineData("not-a-version", null)]
    public void Parse_Version(string tag, string? want)
    {
        Assert.Equal(want, AssetPicker.ParseVersion(tag)?.ToString());
    }

    [Fact]
    public void Install_Id_Distinguishes_Dotnet()
    {
        Assert.Equal("godot-4.3-stable", AssetPicker.InstallId("godot", "4.3-stable", false));
        Assert.Equal("godot-4.3-stable-dotnet", AssetPicker.InstallId("godot", "4.3-stable", true));
    }

    [Fact]
    public void Version_Hint_Reads_First_Features_Entry()
    {
        var dir = Directory.CreateTempSubdirectory("crete-test-").FullName;
        try
        {
            var f = Path.Combine(dir, "project.godot");
            File.WriteAllText(f, "; Engine configuration file.\nconfig/features=PackedStringArray(\"4.3\", \"Forward Plus\")\n");
            Assert.Equal("4.3", EngineService.ReadVersionHint(f));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Capture_Reads_Piped_Output_Without_Deadlock()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var got = EngineService.Capture(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/c echo hello-capture", Path.GetTempPath(), 15);
        Assert.Contains("hello-capture", got);
    }

    [Fact]
    public void FindEditorExe_Skips_Console_Binary()
    {
        var dir = Directory.CreateTempSubdirectory("crete-exe-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Godot_v4.3-stable_win64.console.exe"), "x");
            var want = Path.Combine(dir, "Godot_v4.3-stable_win64.exe");
            File.WriteAllText(want, "x");
            Assert.Equal(want, EngineService.FindEditorExe(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CreateProject_Writes_Minimal_ProjectGodot()
    {
        var root = Directory.CreateTempSubdirectory("crete-new-").FullName;
        try
        {
            var svc = new EngineService(root);
            var proj = svc.CreateProject("My Game", Path.Combine(root, "work"), "mobile");
            var text = File.ReadAllText(Path.Combine(proj.Path, "project.godot"));
            Assert.Contains("config/name=\"My Game\"", text);
            Assert.Contains("renderer/rendering_method=\"mobile\"", text);
            Assert.Contains("config_version=5", text);
            Assert.Equal("My Game", proj.Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CreateProject_Rejects_Duplicate_And_Bad_Renderer()
    {
        var root = Directory.CreateTempSubdirectory("crete-new2-").FullName;
        try
        {
            var svc = new EngineService(root);
            svc.CreateProject("Dup", root);
            Assert.Throws<InvalidOperationException>(() => svc.CreateProject("Dup", root));
            Assert.Throws<ArgumentException>(() => svc.CreateProject("X", root, "vulkan2"));
            Assert.Throws<ArgumentException>(() => svc.CreateProject("  ", root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindProjects_Detects_Csproj()
    {
        var root = Directory.CreateTempSubdirectory("crete-scan-").FullName;
        try
        {
            var a = Path.Combine(root, "A");
            Directory.CreateDirectory(a);
            File.WriteAllText(Path.Combine(a, "project.godot"), "config/features=PackedStringArray(\"4.7\")\n");
            File.WriteAllText(Path.Combine(a, "A.csproj"), "<Project/>\n");
            var svc = new EngineService(root);
            var found = svc.FindProjects(root);
            Assert.Single(found);
            Assert.True(found[0].HasCsproj);
            Assert.Equal("4.7", found[0].VersionHint);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Favorites_Toggle_Roundtrips_Case_Insensitive()
    {
        var root = Directory.CreateTempSubdirectory("crete-fav-").FullName;
        try
        {
            var svc = new EngineService(root);
            Assert.Empty(svc.GetFavorites());
            Assert.True(svc.ToggleFavorite("C:/Games/Nimbus"));
            Assert.True(svc.GetFavorites().Contains("c:/games/nimbus"));
            Assert.False(svc.ToggleFavorite("C:/GAMES/NIMBUS"));
            Assert.Empty(svc.GetFavorites());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProjectEngine_Mapping_Roundtrips()
    {
        var root = Directory.CreateTempSubdirectory("crete-map-").FullName;
        try
        {
            var svc = new EngineService(root);
            Assert.Null(svc.GetProjectEngine("C:/Games/A"));
            svc.SetProjectEngine("C:/Games/A", "godot-4.7.2-stable");
            Assert.Equal("godot-4.7.2-stable", svc.GetProjectEngine("c:/games/a"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
