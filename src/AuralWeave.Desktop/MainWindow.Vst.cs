using System.IO;
using System.Security.Cryptography;
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
    private sealed class VstCard
    {
        public string Folder { get; set; } = "";
        public IReadOnlyList<Vst2Candidate> Choices { get; set; } = [];
        public NativeVst2Plugin? Plugin { get; set; }
        public bool Scanning { get; set; }
    }

    private readonly Dictionary<Guid, VstCard> _vstCards = [];
    private readonly VstPluginRegistry _vstRegistry = new();

    private void BuildVstSettings(Panel panel, Guid id)
    {
        if (!_vstCards.TryGetValue(id, out var card))
            _vstCards[id] = card = new VstCard();

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
        var folderEntry = new Grid();
        var hint = new TextBlock
        {
            Text = "Choose VST plugins folder...",
            Margin = new Thickness(10, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Visibility = string.IsNullOrWhiteSpace(folder.Text) ? Visibility.Visible : Visibility.Collapsed
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        folder.TextChanged += (_, _) =>
        {
            card.Folder = folder.Text;
            hint.Visibility = string.IsNullOrWhiteSpace(folder.Text) ? Visibility.Visible : Visibility.Collapsed;
        };
        folderEntry.Children.Add(folder);
        folderEntry.Children.Add(hint);
        pathRow.Children.Add(folderEntry);
        panel.Children.Add(pathRow);

        var pickRow = new DockPanel { Margin = new Thickness(8, 4, 8, 4) };
        var picker = new ComboBox
        {
            ItemsSource = card.Choices,
            DisplayMemberPath = nameof(Vst2Candidate.Name),
            IsEnabled = card.Choices.Count != 0 && !card.Scanning,
            SelectedItem = card.Choices.FirstOrDefault(choice =>
                string.Equals(choice.Path, card.Plugin?.Path, StringComparison.OrdinalIgnoreCase))
                ?? (card.Choices.Count == 1 ? card.Choices[0] : null)
        };
        AutomationProperties.SetName(picker, "VST plugin");
        var load = new Button { Content = "Load plugin", IsEnabled = !card.Scanning && picker.SelectedItem is Vst2Candidate,
                                Margin = new Thickness(6, 0, 0, 0) };
        picker.SelectionChanged += (_, _) => load.IsEnabled = picker.SelectedItem is Vst2Candidate;
        load.Click += (_, _) =>
        {
            if (picker.SelectedItem is Vst2Candidate choice) LoadVstPlugin(id, card, choice);
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

    private int VstSampleRate()
    {
        var rate = _route?.InputSampleRate ?? (InputCombo.SelectedItem as AudioEndpoint)?.SampleRate ?? 0;
        return rate > 0 ? rate : 48000;
    }

    private void LoadVstPlugin(Guid id, VstCard card, Vst2Candidate choice)
    {
        if (string.Equals(card.Plugin?.Path, choice.Path, StringComparison.OrdinalIgnoreCase)) return;

        var index = _filters.FindIndex(x => x.Id == id);
        if (index < 0) return;

        NativeVst2Plugin? candidate = null;
        var previous = card.Plugin;
        VstPluginReference reference;
        try
        {
            using var dll = File.OpenRead(choice.Path);
            reference = VstPluginRegistry.ReferenceFor(choice.Path, Convert.ToHexString(SHA256.HashData(dll)));
            candidate = new NativeVst2Plugin(choice.Path, VstSampleRate());
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
        _filters[index] = _filters[index] with { VstPlugin = reference };
        RefreshFilterRows();
        var message = $"Loaded VST2 plugin '{choice.Name}'.";
        try { _vstRegistry.Remember(reference); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status(message + " Could not remember this DLL for future sessions.", true);
            return;
        }
        Status(message);
    }

    private Dictionary<Guid, VstCard> RestoreVstCards(List<FilterStage> filters)
    {
        var cards = new Dictionary<Guid, VstCard>();
        var rate = VstSampleRate();
        for (var i = 0; i < filters.Count; i++)
        {
            var stage = filters[i];
            if (stage.VstPlugin is not { } reference) continue;
            var path = VstPluginRegistry.Resolve(reference);
            if (path is null || !Vst2PluginCatalog.IsVst2Plugin(path))
            {
                filters[i] = stage with { VstPlugin = null };
                continue;
            }

            var choice = new Vst2Candidate(Path.GetFileNameWithoutExtension(path), path);
            var card = new VstCard { Folder = Path.GetDirectoryName(path)!, Choices = [choice] };
            if (_vstRegistry.IsKnown(reference))
            {
                try
                {
                    // Keep the scanned file read-locked through the loader,
                    // and only auto-load a binary selected by the user before.
                    using var dll = File.OpenRead(path);
                    var hash = Convert.ToHexString(SHA256.HashData(dll));
                    if (string.Equals(hash, reference.Sha256, StringComparison.OrdinalIgnoreCase))
                        card.Plugin = new NativeVst2Plugin(path, rate);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    filters[i] = stage with { VstPlugin = null };
                    continue;
                }
            }
            cards.Add(stage.Id, card);
        }
        return cards;
    }

    private static Dictionary<Guid, NativeVst2Plugin> LoadedVstPlugins(Dictionary<Guid, VstCard> cards) =>
        cards.Where(pair => pair.Value.Plugin is not null)
             .ToDictionary(pair => pair.Key, pair => pair.Value.Plugin!);

    private void PublishFilterChain(NativeWasapiAudioRoute route) =>
        route.ReplaceFilterChain(_filters, LoadedVstPlugins(_vstCards));

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
}
