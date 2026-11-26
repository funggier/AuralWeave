using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using AuralWeave.Audio.Windows;
using AuralWeave.Core;
using Microsoft.Win32;

namespace AuralWeave.Desktop;

public partial class MainWindow
{
    private sealed class VstCard(string folder)
    {
        public string Folder { get; set; } = folder;
        public IReadOnlyList<Vst2Candidate> Choices { get; set; } = [];
        public NativeVst2Plugin? Plugin { get; set; }
        public bool Scanning { get; set; }
    }

    private readonly Dictionary<Guid, VstCard> _vstCards = [];
    private static readonly string VstFolderFile =
        Path.Combine(AppContext.BaseDirectory, "settings", "vst-folder.txt");

    private void BuildVstSettings(Panel panel, Guid id)
    {
        if (!_vstCards.TryGetValue(id, out var card))
            _vstCards[id] = card = new VstCard(ReadVstFolder());

        var pathRow = new DockPanel { Margin = new Thickness(8, 10, 8, 4) };
        var folder = new TextBox { Text = card.Folder, IsEnabled = !card.Scanning };
        AutomationProperties.SetName(folder, "VST plugins folder");
        folder.ToolTip = "Folder containing the installed 64-bit VST2 DLLs";

        var scan = new Button { Content = card.Scanning ? "Scanning..." : "Scan", IsEnabled = !card.Scanning };
        scan.Click += async (_, _) => await ScanVstFolderAsync(id, card);
        DockPanel.SetDock(scan, Dock.Right);
        pathRow.Children.Add(scan);

        var browse = new Button { Content = "Browse...", IsEnabled = !card.Scanning, Margin = new Thickness(6, 0, 6, 0) };
        browse.Click += async (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = "Choose a VST plugins folder" };
            if (Directory.Exists(card.Folder)) dialog.InitialDirectory = card.Folder;
            if (dialog.ShowDialog(this) != true) return;
            folder.Text = dialog.FolderName;
            await ScanVstFolderAsync(id, card);
        };
        DockPanel.SetDock(browse, Dock.Right);
        pathRow.Children.Add(browse);
        folder.TextChanged += (_, _) => card.Folder = folder.Text;
        pathRow.Children.Add(folder);
        panel.Children.Add(pathRow);

        var pickRow = new DockPanel { Margin = new Thickness(8, 4, 8, 4) };
        var picker = new ComboBox
        {
            ItemsSource = card.Choices,
            DisplayMemberPath = nameof(Vst2Candidate.Name),
            IsEnabled = card.Choices.Count != 0 && !card.Scanning,
            SelectedItem = card.Choices.FirstOrDefault(choice =>
                string.Equals(choice.Path, card.Plugin?.Path, StringComparison.OrdinalIgnoreCase))
        };
        AutomationProperties.SetName(picker, "VST plugin");
        var load = new Button { Content = "Load plugin", IsEnabled = picker.SelectedItem is Vst2Candidate,
                                Margin = new Thickness(6, 0, 0, 0) };
        picker.SelectionChanged += (_, _) => load.IsEnabled = picker.SelectedItem is Vst2Candidate;
        load.Click += (_, _) =>
        {
            if (picker.SelectedItem is Vst2Candidate choice) LoadVstPlugin(card, choice);
        };
        DockPanel.SetDock(load, Dock.Right);
        pickRow.Children.Add(load);
        pickRow.Children.Add(picker);
        panel.Children.Add(pickRow);

        if (card.Plugin is { } plugin)
        {
            var active = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
            var editor = new Button { Content = "Open editor", IsEnabled = plugin.HasEditor };
            editor.Click += (_, _) =>
            {
                try { plugin.ShowEditor(new WindowInteropHelper(this).Handle); }
                catch (Exception ex) { Status("Plugin editor: " + ex.Message, true); }
            };
            DockPanel.SetDock(editor, Dock.Right);
            active.Children.Add(editor);
            active.Children.Add(MutedText(Path.GetFileNameWithoutExtension(plugin.Path)));
            panel.Children.Add(active);
        }
        else
        {
            panel.Children.Add(MutedText(card.Choices.Count == 0
                ? "Choose a folder and scan for 64-bit VST2 plugins."
                : "Select an effect and click Load plugin."));
        }
    }

    private async Task ScanVstFolderAsync(Guid id, VstCard card)
    {
        if (card.Scanning) return;
        var folder = card.Folder.Trim();
        if (folder.Length == 0)
        {
            Status("Enter a VST plugins folder.", true);
            return;
        }
        card.Scanning = true;
        RefreshFilterRows();
        try
        {
            var fullPath = Path.GetFullPath(folder, AppContext.BaseDirectory);
            card.Choices = await Task.Run(() => Vst2PluginCatalog.Scan(fullPath));
            if (!_vstCards.TryGetValue(id, out var current) || current != card) return;

            card.Folder = fullPath;
            SaveVstFolder(fullPath);
            Status(card.Choices.Count == 0 ? "No 64-bit VST2 DLLs found."
                : $"Found {card.Choices.Count} VST2 plugin(s).");
        }
        catch (Exception ex)
        {
            Status("VST scan: " + ex.Message, true);
        }
        finally
        {
            card.Scanning = false;
            if (_vstCards.TryGetValue(id, out var current) && current == card)
                RefreshFilterRows();
        }
    }

    private void LoadVstPlugin(VstCard card, Vst2Candidate choice)
    {
        if (string.Equals(card.Plugin?.Path, choice.Path, StringComparison.OrdinalIgnoreCase)) return;

        NativeVst2Plugin? candidate = null;
        var previous = card.Plugin;
        try
        {
            var rate = _route?.InputSampleRate ?? (InputCombo.SelectedItem as AudioEndpoint)?.SampleRate ?? 0;
            candidate = new NativeVst2Plugin(choice.Path, rate > 0 ? rate : 48000);
            card.Plugin = candidate;
            if (_route is { } route) PublishFilterChain(route);
        }
        catch (Exception ex)
        {
            card.Plugin = previous;
            candidate?.Dispose();
            Status("VST plugin: " + ex.Message, true);
            return;
        }

        previous?.Dispose();
        RefreshFilterRows();
        Status($"Loaded VST2 plugin '{choice.Name}'.");
    }

    private void PublishFilterChain(NativeWasapiAudioRoute route)
    {
        var plugins = new Dictionary<Guid, NativeVst2Plugin>();
        foreach (var (id, card) in _vstCards)
            if (card.Plugin is { } plugin) plugins.Add(id, plugin);
        route.ReplaceFilterChain(_filters, plugins);
    }

    private void PrepareVstPlugins(AudioEndpoint input)
    {
        var plugins = _vstCards.Values.Select(x => x.Plugin).OfType<NativeVst2Plugin>().ToArray();
        if (plugins.Length == 0) return;
        // Initialize on this STA before the route blocks waiting for its WASAPI worker.
        var rate = NativeAudioDeviceCatalog.CaptureDevices()
            .FirstOrDefault(x => x.Id == input.Id)?.SampleRate ?? input.SampleRate;
        foreach (var plugin in plugins)
            plugin.Configure(rate > 0 ? rate : 48000);
    }

    private void RemoveVstCard(Guid id)
    {
        if (_vstCards.Remove(id, out var card)) card.Plugin?.Dispose();
    }

    private void CloseVstCards()
    {
        foreach (var card in _vstCards.Values) card.Plugin?.Dispose();
        _vstCards.Clear();
    }

    private static string ReadVstFolder()
    {
        try
        {
            return File.Exists(VstFolderFile)
                ? Path.GetFullPath(File.ReadAllText(VstFolderFile).Trim(), AppContext.BaseDirectory)
                : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "";
        }
    }

    private static void SaveVstFolder(string folder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VstFolderFile)!);
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, folder);
        var insidePortableFolder = !Path.IsPathRooted(relative) && relative != ".." &&
                                   !relative.StartsWith(@"..\", StringComparison.Ordinal);
        File.WriteAllText(VstFolderFile, insidePortableFolder ? relative : folder);
    }
}
