using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AuralWeave.Audio.Windows;
using AuralWeave.Core;
using Microsoft.Win32;

namespace AuralWeave.Desktop;

public partial class MainWindow
{
    private readonly FilterPresetStore _presetStore = new(
        Path.Combine(AppContext.BaseDirectory, "presets", "filters"));
    private bool _virtualRefreshPending;

    private void ShowStudioModeClicked(object sender, RoutedEventArgs e)
    {
        StudioPanel.Visibility = Visibility.Visible;
        VirtualDriverPanel.Visibility = Visibility.Collapsed;
        StudioHeaderActions.Visibility = Visibility.Visible;
        StudioModeButton.SetResourceReference(Control.BackgroundProperty, "Accent");
        StudioModeButton.SetResourceReference(Control.ForegroundProperty, "AccentText");
        VirtualModeButton.SetResourceReference(Control.BackgroundProperty, "SubtleBackground");
        VirtualModeButton.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
    }

    private async void ShowVirtualDriverModeClicked(object sender, RoutedEventArgs e)
    {
        StudioPanel.Visibility = Visibility.Collapsed;
        VirtualDriverPanel.Visibility = Visibility.Visible;
        // If routing is running, keep STOP accessible even from the manager.
        StudioHeaderActions.Visibility = _route is not null
            ? Visibility.Visible : Visibility.Collapsed;
        VirtualModeButton.SetResourceReference(Control.BackgroundProperty, "Accent");
        VirtualModeButton.SetResourceReference(Control.ForegroundProperty, "AccentText");
        StudioModeButton.SetResourceReference(Control.BackgroundProperty, "SubtleBackground");
        StudioModeButton.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
        await RefreshVirtualDriversAsync();
    }

    private void RefreshPresetsClicked(object sender, RoutedEventArgs e) =>
        RefreshPresetList();

    private void RefreshPresetList(string? selectName = null)
    {
        try
        {
            var previous = selectName ?? (PresetCombo.SelectedItem as FilterPresetItem)?.Name;
            var list = _presetStore.List();
            PresetCombo.ItemsSource = list;
            PresetCombo.SelectedItem =
                list.FirstOrDefault(x => x.Name.Equals(previous,
                    StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Status("Preset list could not be loaded: " + ex.Message, true);
        }
    }

    private string CurrentPresetName()
    {
        var name = PresetNameBox.Text.Trim();
        return name.Length == 0 ? "My Filters" : name;
    }

    private void SavePresetClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = _presetStore.Create(CurrentPresetName(), _filters);
            var file = Path.Combine(_presetStore.Folder, preset.Name +
                FilterPresetStore.Extension);
            var overwrite = File.Exists(file);
            if (overwrite && MessageBox.Show(this,
                    $"Replace existing preset '{preset.Name}'?",
                    "AuralWeave — Save Filter Preset", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _presetStore.Save(preset, overwrite);
            RefreshPresetList(preset.Name);
            Status($"Saved Filter preset: {preset.Name} (input/output not included).");
        }
        catch (Exception ex)
        {
            Status("Cannot save Filter preset: " + ex.Message, true);
        }
    }

    private void LoadPresetClicked(object sender, RoutedEventArgs e)
    {
        if (PresetCombo.SelectedItem is not FilterPresetItem selected)
        {
            Status("Select a saved Filter preset first.", true);
            return;
        }
        try
        {
            var preset = FilterPresetStore.ReadFile(selected.FilePath);
            LoadPresetIntoStudio(preset);
        }
        catch (Exception ex)
        {
            Status("Cannot load Filter preset: " + ex.Message, true);
        }
    }

    private void LoadPresetIntoStudio(FilterPresetDocument preset)
    {
        var restored = FilterPresetStore.Restore(preset);
        // Update native DSP first. Any invalid preset leaves the existing route
        // and current UI filters untouched. Sound Input / Output never change.
        _route?.ReplaceFilterChain(restored);
        _filterDebounceTimer.Stop();
        _filters.Clear();
        _filters.AddRange(restored);
        _collapsedFilters.Clear();
        _collapseAllNext = true;
        PresetNameBox.Text = preset.Name;
        RefreshFilterRows();
        Status($"Loaded '{preset.Name}' — {restored.Count} filter(s); audio devices unchanged.");
    }

    private void ExportPresetClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var preset = _presetStore.Create(CurrentPresetName(), _filters);
            var dialog = new SaveFileDialog
            {
                Title = "Export AuralWeave Filters",
                Filter = "AuralWeave Filter Preset (*.awfilter)|*.awfilter",
                DefaultExt = FilterPresetStore.Extension,
                AddExtension = true,
                FileName = preset.Name + FilterPresetStore.Extension,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;
            FilterPresetStore.WriteFile(dialog.FileName, preset, overwrite: true);
            Status($"Exported Filter preset: {preset.Name}.");
        }
        catch (Exception ex)
        {
            Status("Cannot export Filter preset: " + ex.Message, true);
        }
    }

    private void ImportPresetClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import AuralWeave Filters",
            Filter = "AuralWeave Filter Preset (*.awfilter)|*.awfilter",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var preset = FilterPresetStore.ReadFile(dialog.FileName);
            var destination = Path.Combine(_presetStore.Folder,
                preset.Name + FilterPresetStore.Extension);
            var overwrite = File.Exists(destination);
            if (overwrite && MessageBox.Show(this,
                    $"Replace locally saved preset '{preset.Name}'?",
                    "AuralWeave — Import Filter Preset", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            // Validate and apply before saving. An invalid preset cannot
            // corrupt the existing live route or local preset collection.
            LoadPresetIntoStudio(preset);
            _presetStore.Save(preset, overwrite);
            RefreshPresetList(preset.Name);
            Status($"Imported '{preset.Name}' into portable Filter presets.");
        }
        catch (Exception ex)
        {
            Status("Cannot import Filter preset: " + ex.Message, true);
        }
    }

    private async void RefreshVirtualDriversClicked(object sender, RoutedEventArgs e) =>
        await RefreshVirtualDriversAsync();

    private async Task RefreshVirtualDriversAsync()
    {
        if (_virtualRefreshPending) return;
        _virtualRefreshPending = true;
        RefreshVirtualDriversButton.IsEnabled = false;
        VirtualDriverSummary.Text = "Searching installed AuralWeave-family PnP devices...";
        try
        {
            var installed = await Task.Run(
                AuralWeaveVirtualDrivers.EnumerateInstalled);
            var inputDevices = installed.Where(
                x => x.Role == VirtualAudioEndpointRole.Input).ToArray();
            var outputDevices = installed.Where(
                x => x.Role == VirtualAudioEndpointRole.Output).ToArray();
            FillVirtualDeviceCard(VirtualInputDevices, inputDevices,
                "No AuralWeave Virtual Input installed (0/1).");
            FillVirtualDeviceCard(VirtualOutputDevices, outputDevices,
                "No AuralWeave Virtual Output installed (0/1).");

            var activeInput = inputDevices.Count(x => !x.IsLegacy);
            var activeOutput = outputDevices.Count(x => !x.IsLegacy);
            var legacy = installed.Count(x => x.IsLegacy);
            VirtualInputPackageStatus.Text = activeInput > 0
                ? $"Virtual Input: {activeInput}/1 installed. Maximum reached; no additional installation."
                : "Virtual Input: 0/1. Installation unavailable until audio bridge and driver signing are complete.";
            VirtualOutputPackageStatus.Text = activeOutput > 0
                ? $"Virtual Output: {activeOutput}/1 installed. Maximum reached; no additional installation."
                : "Virtual Output: 0/1. Installation unavailable until audio bridge and driver signing are complete.";

            var conflict = activeInput > 1 || activeOutput > 1 ||
                           (legacy > 0);
            VirtualDriverSummary.Text =
                $"Virtual Input {activeInput}/1 · Virtual Output {activeOutput}/1. " +
                (legacy > 0
                    ? $"{legacy} legacy PnP device(s) require optional explicit cleanup. "
                    : "") +
                (conflict
                    ? "Review devices before any future installation. "
                    : "") +
                "Discovery uses PnP identity, independent of the portable app folder. " +
                "Unsigned/unbridged kernel prototypes cannot be installed.";
            // No signed kernel package yet: keep all INSTALL buttons disabled
            // regardless of inventory. Future install must also enforce
            // AuralWeaveVirtualDrivers.MayInstall(...).
            InstallVirtualInputButton.IsEnabled = false;
            InstallVirtualOutputButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            VirtualDriverSummary.Text = "PnP enumeration failed: " + ex.Message;
            Status("Virtual device discovery failed: " + ex.Message, true);
        }
        finally
        {
            _virtualRefreshPending = false;
            RefreshVirtualDriversButton.IsEnabled = true;
        }
    }

    private void FillVirtualDeviceCard(StackPanel panel,
        IReadOnlyList<AuralWeaveVirtualDevice> devices, string emptyText)
    {
        panel.Children.Clear();
        if (devices.Count == 0)
        {
            panel.Children.Add(MutedText(emptyText));
            return;
        }
        foreach (var device in devices)
        {
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11)
            };
            row.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
            row.SetResourceReference(Border.BackgroundProperty, "SubtleBackground");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition {
                Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var details = new StackPanel();
            details.Children.Add(new TextBlock
            {
                Text = device.IsLegacy ? device.Name + " (LEGACY — cleanup only)"
                                       : device.Name,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            var identity = MutedText(device.InstanceId);
            identity.FontSize = 10;
            identity.ToolTip = device.HardwareId;
            details.Children.Add(identity);
            grid.Children.Add(details);
            var remove = new Button
            {
                Content = "UNINSTALL",
                ToolTip = device.IsLegacy
                    ? "Uninstall this obsolete AuralWeave prototype device only"
                    : "Uninstall only this AuralWeave-owned PnP device",
                MinWidth = 106,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Grid.SetColumn(remove, 1);
            remove.Click += async (_, _) => await UninstallVirtualDeviceAsync(device);
            grid.Children.Add(remove);
            row.Child = grid;
            panel.Children.Add(row);
        }
    }

    private async Task UninstallVirtualDeviceAsync(AuralWeaveVirtualDevice device)
    {
        var result = MessageBox.Show(this,
            $"Uninstall this AuralWeave virtual device from Windows?\n\n" +
            $"{device.Name}\n{device.InstanceId}\n\n" +
            "Administrator approval will be required. Other audio drivers will not be touched.",
            "AuralWeave — Confirm Uninstall", MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var exitCode = await AuralWeaveVirtualDrivers.RequestDeviceRemovalAsync(device);
            Status(exitCode == 0
                ? $"Removed virtual device: {device.Name}."
                : $"Windows PnPUtil reported exit code {exitCode} while removing {device.Name}.",
                exitCode != 0);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status("Windows administrator approval was cancelled.");
        }
        catch (Exception ex)
        {
            Status("Virtual device removal failed: " + ex.Message, true);
        }
        await RefreshVirtualDriversAsync();
    }

    private void InstallVirtualInputClicked(object sender, RoutedEventArgs e) =>
        Status("A signed, installable AuralWeave Virtual Input driver has not been built.", true);

    private void InstallVirtualOutputClicked(object sender, RoutedEventArgs e) =>
        Status("A signed, installable AuralWeave Virtual Output driver has not been built.", true);
}
