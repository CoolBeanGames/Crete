using System.IO;
using CreteLauncher.Core;

namespace CreteLauncher;

/// <summary>Headless commands so every launcher operation works (and is verifiable) without the GUI.</summary>
public static class Headless
{
    public static async Task<int> RunAsync(string[] args)
    {
        var svc = new EngineService();
        var cmd = args[0].ToLowerInvariant();
        bool has(string f) => args.Any(a => a.Equals(f, StringComparison.OrdinalIgnoreCase));
        string? opt(string f)
        {
            var i = Array.FindIndex(args, a => a.Equals(f, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        switch (cmd)
        {
            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;
            case "list-remote":
            {
                var ch = opt("--channel") ?? args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "godot";
                var showAll = has("--all");
                var rels = (await svc.ListRemoteAsync(ch, 100))
                    .Where(r => showAll || AssetPicker.IsStableRelease(r))
                    .Take(showAll ? 100 : 20);
                foreach (var r in rels)
                {
                    var std = AssetPicker.PickWindowsEditor(r.Assets, false) is not null ? "win64" : "-";
                    var dn = AssetPicker.PickWindowsEditor(r.Assets, true) is not null ? "dotnet" : "-";
                    Console.WriteLine($"{r.Tag}  [{std} {dn}]  {r.Name}");
                }
                return 0;
            }
            case "install":
            {
                var ch = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-')) ?? "godot";
                var tag = opt("--version");
                var dotnet = has("--dotnet");
                var last = -1;
                var prog = new Progress<DownloadProgress>(p =>
                {
                    var pct = (int)(p.Fraction * 100);
                    if (pct != last && pct % 5 == 0)
                    {
                        last = pct;
                        Console.Write($"\r  downloading... {pct}% ({p.BytesReceived / 1048576} MiB)");
                    }
                });
                Console.WriteLine($"Installing {(dotnet ? ".NET " : "")}{ch} {(tag ?? "(latest stable)")}...");
                var eng = await svc.InstallAsync(ch, tag, dotnet, prog);
                Console.WriteLine($"\nInstalled {eng.Id}\n  {eng.ExePath}");
                return 0;
            }
            case "import":
            {
                var dir = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
                    ?? throw new ArgumentException("usage: import <build-dir> [--id ID] [--tag TAG]");
                var last = -1;
                var prog = new Progress<DownloadProgress>(p =>
                {
                    var pct = (int)(p.Fraction * 100);
                    if (pct != last && pct % 10 == 0)
                    {
                        last = pct;
                        Console.Write($"\r  copying... {pct}% ({p.BytesReceived / 1048576} MiB)");
                    }
                });
                Console.WriteLine($"Importing local engine from {dir}...");
                var eng = await svc.ImportLocalAsync(dir, opt("--id"), opt("--tag"), prog);
                Console.WriteLine($"\nImported {eng.Id}\n  {eng.ExePath}");
                return 0;
            }
            case "list":
            {
                var installed = svc.ListInstalled();
                if (installed.Count == 0)
                    Console.WriteLine("(nothing installed yet)");
                foreach (var i in installed)
                    Console.WriteLine($"{i.Id}  ->  {i.ExePath}");
                return 0;
            }
            case "verify":
            {
                var id = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
                    ?? throw new ArgumentException("usage: verify <install-id>");
                Console.WriteLine(svc.Verify(id));
                return 0;
            }
            case "launch":
            {
                var id = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
                    ?? throw new ArgumentException("usage: launch <install-id> [--project PATH]");
                svc.Launch(id, opt("--project"));
                Console.WriteLine($"Launched {id}.");
                return 0;
            }
            case "new":
            {
                var name = opt("--name") ?? throw new ArgumentException("usage: new --name NAME [--dir DIR] [--renderer R] [--engine ID]");
                var dir = opt("--dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crete");
                var engine = opt("--engine");
                if (engine is not null)
                    svc.RequireInstalled(engine);
                var proj = svc.CreateProject(name, dir, opt("--renderer") ?? "forward_plus");
                if (engine is not null)
                    svc.SetProjectEngine(proj.Path, engine);
                Console.WriteLine($"Created {proj.Name}\n  {proj.Path}" + (engine is null ? "" : $"\n  engine: {engine}"));
                return 0;
            }
            case "projects":
            {
                var dir = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'))
                    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crete");
                var found = svc.FindProjects(dir);
                if (found.Count == 0)
                    Console.WriteLine($"(no project.godot found under {dir})");
                foreach (var p in found)
                    Console.WriteLine($"{p.Name}  [{p.VersionHint ?? "?"}]  [{svc.GetProjectEngine(p.Path) ?? "-"}]  {p.Path}");
                return 0;
            }
            default:
                Console.WriteLine($"Unknown command '{cmd}'.");
                PrintHelp();
                return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CreteLauncher — headless commands (run with no args for the GUI):
              list-remote [godot|crete] [--all]   list downloadable engine versions
              install <channel> [--version TAG] [--dotnet]
              import <build-dir> [--id ID] [--tag TAG]
              list                                installed versions
              verify <install-id>                 run editor --version to prove the install
              launch <install-id> [--project P]   open the editor GUI (optionally with a project)
              new --name N [--dir D] [--renderer R] [--engine ID]
              projects [DIR]                      scan for project.godot files
            """);
    }
}
