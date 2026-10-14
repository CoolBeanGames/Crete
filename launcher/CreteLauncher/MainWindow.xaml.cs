using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CreteLauncher.Core;
using Microsoft.Win32;

namespace CreteLauncher;

public sealed class ProjectRow
{
    private static readonly SolidColorBrush StarOn = new((Color)ColorConverter.ConvertFromString("#F5C445"));
    private static readonly SolidColorBrush StarOff = new((Color)ColorConverter.ConvertFromString("#8A8A8E"));

    public ProjectRow(GameProject project, List<InstalledEngine> engines, InstalledEngine? def, bool isFav)
    {
        Project = project;
        Engines = engines;
        SelectedEngine = def;
        IsFavorite = isFav;
        StarBrush = isFav ? StarOn : StarOff;
        ThumbIndex = Math.Abs(project.Name.Sum(c => (int)c)) % 6;
    }

    public GameProject Project { get; }
    public List<InstalledEngine> Engines { get; }
    public InstalledEngine? SelectedEngine { get; set; }
    public bool IsFavorite { get; }
    public Brush StarBrush { get; }
    public int ThumbIndex { get; }
    public bool HasCsproj => Project.HasCsproj;
}

public sealed class TemplateOption : INotifyPropertyChanged
{
    private static readonly SolidColorBrush SelOn = new((Color)ColorConverter.ConvertFromString("#1E9DF0"));
    private static readonly SolidColorBrush SelOff = new((Color)ColorConverter.ConvertFromString("#3D3D40"));

    public TemplateOption(string name, string desc, string renderer, int thumbIndex)
    {
        Name = name;
        Desc = desc;
        Renderer = renderer;
        ThumbIndex = thumbIndex;
    }

    public string Name { get; }
    public string Desc { get; }
    public string Renderer { get; }
    public int ThumbIndex { get; }

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelBorder)));
        }
    }

    public Brush SelBorder => Selected ? SelOn : SelOff;
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class RemoteRow
{
    public RemoteRow(EngineRelease release)
    {
        Release = release;
        var std = AssetPicker.PickWindowsEditor(release.Assets, false) is not null;
        var dn = AssetPicker.PickWindowsEditor(release.Assets, true) is not null;
        Details = $"{release.Name} · win64 {(std ? "✓" : "✕")} · dotnet {(dn ? "✓" : "✕")}";
    }

    public EngineRelease Release { get; }
    public string Details { get; }
}

public sealed class ThumbSelector : DataTemplateSelector
{
    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        var i = item switch
        {
            ProjectRow r => r.ThumbIndex,
            TemplateOption t => t.ThumbIndex,
            _ => 0,
        };
        if (container is FrameworkElement fe && fe.FindResource($"Thumb{i}") is DataTemplate d)
            return d;
        return base.SelectTemplate(item, container);
    }
}

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush Accent = new((Color)ColorConverter.ConvertFromString("#1E9DF0"));
    private static readonly SolidColorBrush AccentDim = new((Color)ColorConverter.ConvertFromString("#24384A"));
    private static readonly SolidColorBrush BtnBg = new((Color)ColorConverter.ConvertFromString("#3A3A3D"));
    private static readonly SolidColorBrush BtnBorder = new((Color)ColorConverter.ConvertFromString("#4A4A4E"));
    private static readonly SolidColorBrush TransparentBg = new(Colors.Transparent);
    private static readonly SolidColorBrush MutedFg = new((Color)ColorConverter.ConvertFromString("#A9A9AE"));
    private static readonly SolidColorBrush TextFg = new((Color)ColorConverter.ConvertFromString("#ECECEC"));

    private static readonly List<TemplateOption> Templates =
    [
        new("Blank 3D", "Empty scene, sun + camera. Forward+.", "forward_plus", 1),
        new("Blank 2D", "Pixel-perfect default. Compatibility renderer.", "gl_compatibility", 0),
        new("3D Platformer", "CharacterBody3D + coyote time. Forward+.", "forward_plus", 1),
        new("2D Platformer", "TileMap + character controller. Compatibility.", "gl_compatibility", 0),
        new("Top-Down RPG", "Y-sort, dialogue + inventory stubs. Compatibility.", "gl_compatibility", 5),
        new("Multiplayer", "Steam lobby + ENet sample. Forward+.", "forward_plus", 2),
        new("Steam Demo", "Achievements + cloud saves wired. Forward+.", "forward_plus", 3),
    ];

    private readonly EngineService _svc = new();
    private List<GameProject> _allProjects = [];
    private List<InstalledEngine> _engines = [];
    private string _chip = "all";
    private string _channel = "godot";
    private bool _dotnet;
    private string _renderer = "forward_plus";

    public MainWindow()
    {
        InitializeComponent();
        var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crete");
        ProjectLocation.Text = docs;
        TemplateList.ItemsSource = Templates;
        TemplateList.SelectedIndex = 0;
        Loaded += async (_, _) =>
        {
            ShowView("projects");
            PaintChips("all");
            PaintSeg("grid");
            SetChannel("godot");
            SetRenderer("forward_plus");
            await RefreshInstalledAsync();
            ScanProjects(docs);
        };
    }

    // ---------- window chrome ----------
    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaxButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- navigation ----------
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string view)
        {
            if (view == "projects")
                ScanProjects(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crete"));
            ShowView(view);
        }
    }

    private void GoNew_Click(object sender, RoutedEventArgs e) => ShowView("new");

    private void ShowView(string view)
    {
        ProjectsView.Visibility = view == "projects" ? Visibility.Visible : Visibility.Collapsed;
        NewView.Visibility = view == "new" ? Visibility.Visible : Visibility.Collapsed;
        InstallsView.Visibility = view == "installs" ? Visibility.Visible : Visibility.Collapsed;
        ChipsRow.Visibility = view == "projects" ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Visibility = view == "projects" ? Visibility.Visible : Visibility.Collapsed;
        SegGrid.Visibility = view == "projects" ? Visibility.Visible : Visibility.Collapsed;
        SegList.Visibility = view == "projects" ? Visibility.Visible : Visibility.Collapsed;
        ViewTitle.Text = view switch { "new" => "New Project", "installs" => "Installs", _ => "Projects" };
        PaintNav(NavProjects, view == "projects");
        PaintNav(NavNew, view == "new");
        PaintNav(NavInstalls, view == "installs");
    }

    private static void PaintNav(Button b, bool active)
    {
        b.Background = active ? AccentDim : TransparentBg;
        b.Foreground = active ? TextFg : MutedFg;
    }

    // ---------- projects ----------
    private void ScanProjects(string root)
    {
        try
        {
            _allProjects = _svc.FindProjects(root).ToList();
            RenderProjects();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            StatusLeft.Text = "error: " + ex.Message;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RenderProjects();

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string chip)
        {
            _chip = chip;
            PaintChips(chip);
            RenderProjects();
        }
    }

    private void PaintChips(string chip)
    {
        foreach (var child in ChipsRow.Children)
        {
            if (child is Button c)
            {
                var on = c.Tag as string == chip;
                c.Background = on ? AccentDim : TransparentBg;
                c.BorderBrush = on ? Accent : BtnBorder;
                c.Foreground = on ? TextFg : MutedFg;
            }
        }
    }

    private void RenderProjects()
    {
        if (ProjectsGrid is null)
            return;
        var q = (SearchBox?.Text ?? "").Trim().ToLowerInvariant();
        var favs = _svc.GetFavorites();
        var rows = _allProjects
            .Where(p => _chip switch
            {
                "fav" => favs.Contains(p.Path),
                "4x" => (p.VersionHint ?? "").StartsWith("4"),
                "3x" => (p.VersionHint ?? "").StartsWith("3"),
                "cs" => p.HasCsproj,
                _ => true,
            })
            .Where(p => q.Length == 0 || p.Name.ToLowerInvariant().Contains(q) || p.Path.ToLowerInvariant().Contains(q))
            .Select(p => new ProjectRow(p, _engines, EngineFor(p), favs.Contains(p.Path)))
            .ToList();
        ProjectsGrid.ItemsSource = rows;
        ProjectsList.ItemsSource = rows;
        ViewTitle.Text = "Projects";
        StatusLeft.Text = $"{_engines.Count} engines · {rows.Count} projects shown";
    }

    private InstalledEngine? EngineFor(GameProject p)
    {
        var mapped = _svc.GetProjectEngine(p.Path);
        return _engines.FirstOrDefault(e => e.Id == mapped) ?? DefaultEngine();
    }

    private void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string v)
        {
            PaintSeg(v);
            GridScroll.Visibility = v == "grid" ? Visibility.Visible : Visibility.Collapsed;
            ListScroll.Visibility = v == "list" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void PaintSeg(string v)
    {
        PaintToggle(SegGrid, v == "grid");
        PaintToggle(SegList, v == "list");
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectRow row })
        {
            _svc.ToggleFavorite(row.Project.Path);
            RenderProjects();
        }
    }

    private void EnginePicker_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { DataContext: ProjectRow row, SelectedItem: InstalledEngine eng })
        {
            if (_svc.GetProjectEngine(row.Project.Path) != eng.Id)
                _svc.SetProjectEngine(row.Project.Path, eng.Id);
        }
    }

    private InstalledEngine? DefaultEngine() =>
        _engines
            .Select(e => (E: e, V: AssetPicker.ParseVersion(e.Tag)))
            .Where(t => t.V is not null)
            .OrderByDescending(t => t.V!.Major >= 4)
            .ThenByDescending(t => t.V)
            .Select(t => t.E)
            .FirstOrDefault();

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ProjectRow row })
        {
            if (row.SelectedEngine is null)
            {
                StatusLeft.Text = "Install an engine first (Installs view).";
                return;
            }
            _svc.Launch(row.SelectedEngine.Id, row.Project.Path);
            StatusLeft.Text = $"Opened {row.Project.Name} in {row.SelectedEngine.Id}.";
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() != true)
            return;
        if (!File.Exists(Path.Combine(dlg.FolderName, "project.godot")))
        {
            StatusLeft.Text = "That folder has no project.godot.";
            return;
        }
        var eng = DefaultEngine();
        if (eng is null)
        {
            StatusLeft.Text = "Install an engine first (Installs view).";
            return;
        }
        _svc.Launch(eng.Id, dlg.FolderName);
        StatusLeft.Text = $"Opened {dlg.FolderName} in {eng.Id}.";
    }

    private void UpdateStatus()
    {
        StatusLeft.Text = $"{_engines.Count} engines · {_allProjects.Count} projects";
        StatusRight.Text = _svc.DataDir;
        SideStatus.Text = _engines.Count == 0
            ? "No engines installed yet."
            : $"{_engines.Count} version{(_engines.Count == 1 ? "" : "s")} ready · {(DefaultEngine()?.Id ?? "")}";
    }

    // ---------- installs ----------
    private void Channel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string ch)
        {
            SetChannel(ch);
            _ = RefreshRemoteAsync();
        }
    }

    private void SetChannel(string ch)
    {
        _channel = ch;
        PaintToggle(ChanGodot, ch == "godot");
        PaintToggle(ChanCrete, ch == "crete");
    }

    private static void PaintToggle(Button b, bool active)
    {
        b.Background = active ? Accent : BtnBg;
        b.BorderBrush = active ? Accent : BtnBorder;
        b.Foreground = TextFg;
        b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshRemoteAsync();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var open = AddPanel.Visibility != Visibility.Visible;
        AddPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AddButton.Content = open ? "－ Add engine version" : "＋ Add engine version";
        if (open && RemoteRows.ItemsSource is null)
            _ = RefreshRemoteAsync();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Pick a local engine build folder (contains the editor .exe)" };
        if (dlg.ShowDialog() != true)
            return;
        ImportButton.IsEnabled = false;
        try
        {
            var prog = new Progress<DownloadProgress>(p => InstallProgress.Value = p.Fraction * 100);
            InstallStatus.Text = "Importing local build…";
            var eng = await _svc.ImportLocalAsync(dlg.FolderName, progress: prog);
            InstallStatus.Text = $"Imported {eng.Id}";
            await RefreshInstalledAsync();
            RenderProjects();
        }
        catch (Exception ex)
        {
            InstallStatus.Text = "error: " + ex.Message;
        }
        finally
        {
            InstallProgress.Value = 0;
            ImportButton.IsEnabled = true;
        }
    }

    private async Task RefreshRemoteAsync()
    {
        InstallStatus.Text = $"Contacting {_channel} channel…";
        try
        {
            var rels = await _svc.ListRemoteAsync(_channel, 100);
            RemoteRows.Tag = _channel;
            var rows = rels.Where(AssetPicker.IsStableRelease).Take(20).Select(r => new RemoteRow(r)).ToList();
            RemoteRows.ItemsSource = rows;
            InstallStatus.Text = $"{rows.Count} stable releases found.";
        }
        catch (Exception ex)
        {
            RemoteRows.ItemsSource = null;
            InstallStatus.Text = "error: " + ex.Message;
        }
    }

    private void FlavorButton_Click(object sender, RoutedEventArgs e)
    {
        _dotnet = !_dotnet;
        FlavorButton.Content = _dotnet ? ".NET build" : "Standard";
        PaintToggle(FlavorButton, _dotnet);
    }

    private async Task RefreshInstalledAsync()
    {
        _engines = (await Task.Run(() => _svc.ListInstalled())).ToList();
        InstalledRows.ItemsSource = _engines;
        EnginePicker.ItemsSource = _engines;
        if (EnginePicker.SelectedItem is null)
            EnginePicker.SelectedItem = DefaultEngine();
        UpdateStatus();
    }

    private async Task InstallAsync(string? tag)
    {
        InstallLatestButton.IsEnabled = false;
        try
        {
            var prog = new Progress<DownloadProgress>(p => InstallProgress.Value = p.Fraction * 100);
            InstallStatus.Text = "Downloading…";
            var eng = await _svc.InstallAsync(_channel, tag, _dotnet, prog);
            InstallStatus.Text = $"Installed {eng.Id}";
            await RefreshInstalledAsync();
            RenderProjects();
        }
        catch (Exception ex)
        {
            InstallStatus.Text = "error: " + ex.Message;
        }
        finally
        {
            InstallProgress.Value = 0;
            InstallLatestButton.IsEnabled = true;
        }
    }

    private async void InstallRemote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RemoteRow row })
            await InstallAsync(row.Release.Tag);
    }

    private async void InstallLatestRemote_Click(object sender, RoutedEventArgs e) => await InstallAsync(null);

    private async void VerifyInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstalledEngine eng })
            return;
        VerifyText.Text = $"Verifying {eng.Id}…";
        try
        {
            VerifyText.Text = await Task.Run(() => _svc.Verify(eng.Id));
        }
        catch (Exception ex)
        {
            VerifyText.Text = "error: " + ex.Message;
        }
    }

    private void LaunchInstalled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InstalledEngine eng })
            _svc.Launch(eng.Id);
    }

    // ---------- new project ----------
    private void TemplateList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateList.SelectedItem is not TemplateOption t)
            return;
        foreach (var opt in Templates)
            opt.Selected = opt == t;
        TemplateDesc.Text = t.Desc;
        SummaryName.Text = t.Name;
        SetRenderer(t.Renderer);
    }

    private void Renderer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string r)
            SetRenderer(r);
    }

    private void SetRenderer(string r)
    {
        _renderer = r;
        PaintToggle(RendForward, r == "forward_plus");
        PaintToggle(RendMobile, r == "mobile");
        PaintToggle(RendCompat, r == "gl_compatibility");
    }

    private void BrowseLocation_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { InitialDirectory = ProjectLocation.Text };
        if (dlg.ShowDialog() == true)
            ProjectLocation.Text = dlg.FolderName;
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var proj = _svc.CreateProject(ProjectName.Text, ProjectLocation.Text, _renderer);
            if (EnginePicker.SelectedItem is InstalledEngine target)
                _svc.SetProjectEngine(proj.Path, target.Id);
            CreateStatus.Text = $"Created {proj.Name} at {proj.Path}";
            ShowView("projects");
            ScanProjects(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Crete"));
        }
        catch (Exception ex)
        {
            CreateStatus.Text = "error: " + ex.Message;
        }
    }
}
