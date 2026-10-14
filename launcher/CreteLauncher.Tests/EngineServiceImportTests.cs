using CreteLauncher.Core;

namespace CreteLauncher.Tests;

public sealed class EngineServiceImportTests
{
    private static string MakeFakeBuild(string root, string exeName = "godot.windows.editor.x86_64.mono.exe")
    {
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(Path.Combine(src, "obj"));
        Directory.CreateDirectory(Path.Combine(src, "build_deps"));
        Directory.CreateDirectory(Path.Combine(src, "GodotSharp"));
        File.WriteAllText(Path.Combine(src, exeName), "fake-exe");
        File.WriteAllText(Path.Combine(src, "D3D12Core.dll"), "fake-dll");
        File.WriteAllText(Path.Combine(src, "GodotSharp", "GodotSharp.dll"), "fake-sharp");
        File.WriteAllText(Path.Combine(src, "obj", "junk.obj"), "junk");
        File.WriteAllText(Path.Combine(src, "build_deps", "junk.a"), "junk");
        File.WriteAllText(Path.Combine(src, "editor.pdb"), "junk");
        File.WriteAllText(Path.Combine(src, "editor.exp"), "junk");
        File.WriteAllText(Path.Combine(src, "editor.lib"), "junk");
        return src;
    }

    [Fact]
    public async Task ImportLocal_Copies_Engine_And_Skips_Build_Junk()
    {
        var root = Directory.CreateTempSubdirectory("crete-import-").FullName;
        try
        {
            var svc = new EngineService(Path.Combine(root, "data"));
            var eng = await svc.ImportLocalAsync(MakeFakeBuild(root), tag: "4.7.3-stable");
            Assert.Equal("crete-local-dotnet", eng.Id);
            Assert.Equal("crete", eng.Channel);
            Assert.Equal("4.7.3-stable", eng.Tag);
            Assert.True(eng.Dotnet);
            Assert.True(File.Exists(eng.ExePath));
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(eng.ExePath)!, "GodotSharp", "GodotSharp.dll")));
            var dest = Path.GetDirectoryName(eng.ExePath)!;
            Assert.False(Directory.Exists(Path.Combine(dest, "obj")));
            Assert.False(Directory.Exists(Path.Combine(dest, "build_deps")));
            Assert.False(File.Exists(Path.Combine(dest, "editor.pdb")));
            Assert.False(File.Exists(Path.Combine(dest, "editor.exp")));
            Assert.False(File.Exists(Path.Combine(dest, "editor.lib")));
            Assert.True(File.Exists(Path.Combine(dest, "installed.json")));
            Assert.Contains(svc.ListInstalled(), e => e.Id == eng.Id);
            Assert.Equal(eng.Id, svc.RequireInstalled(eng.Id).Id);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ImportLocal_Throws_When_No_Exe()
    {
        var root = Directory.CreateTempSubdirectory("crete-import-noexe-").FullName;
        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "readme.txt"), "no exe here");
            var svc = new EngineService(Path.Combine(root, "data"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ImportLocalAsync(src, tag: "x"));
            await Assert.ThrowsAsync<ArgumentException>(() => svc.ImportLocalAsync(Path.Combine(root, "missing"), tag: "x"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ImportLocal_Falls_Back_To_Local_Tag_When_Version_Probe_Fails()
    {
        var root = Directory.CreateTempSubdirectory("crete-import-tag-").FullName;
        try
        {
            // Fake .exe cannot run, so --headless --version fails and the tag is "local".
            var svc = new EngineService(Path.Combine(root, "data"));
            var eng = await svc.ImportLocalAsync(MakeFakeBuild(root));
            Assert.Equal("local", eng.Tag);
            Assert.Equal("crete-local-dotnet", eng.Id);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ImportLocal_Honors_Custom_Id()
    {
        var root = Directory.CreateTempSubdirectory("crete-import-id-").FullName;
        try
        {
            var svc = new EngineService(Path.Combine(root, "data"));
            var eng = await svc.ImportLocalAsync(MakeFakeBuild(root, "mybuild.exe"), installId: "dev-box", tag: "4.7.3-stable");
            Assert.Equal("dev-box", eng.Id);
            Assert.False(eng.Dotnet);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
