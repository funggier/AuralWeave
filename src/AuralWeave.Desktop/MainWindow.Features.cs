using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AuralWeave.Audio.Windows;
using AuralWeave.Core;
using Microsoft.Win32;

namespace AuralWeave.Desktop;

public partial class MainWindow
{
    private readonly FilterPresetStore _presetStore = new(
        Path.Combine(AppContext.BaseDirectory, "presets", "filters"));
    private const int WmDeviceChange = 0x0219;
    private const int DbtDevnodesChanged = 0x0007;
    private readonly DispatcherTimer _virtualRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };
    private bool _virtualRefreshPending;
    private bool _virtualRefreshQueued;
    private bool _virtualInstallPending;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(OnDeviceChange);
        _virtualRefreshTimer.Tick += async (_, _) =>
        {
            _virtualRefreshTimer.Stop();
            if (VirtualDriverPanel.IsVisible)
                await RefreshVirtualDriversAsync();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _virtualRefreshTimer.Stop();
        base.OnClosed(e);
    }

    private IntPtr OnDeviceChange(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        ref bool handled)
    {
        if (message == WmDeviceChange && wParam.ToInt64() == DbtDevnodesChanged)
            QueueVirtualDriverRefresh();
        return IntPtr.Zero;
    }

    private void QueueVirtualDriverRefresh()
    {
        if (!VirtualDriverPanel.IsVisible) return;
        _virtualRefreshTimer.Stop();
        _virtualRefreshTimer.Start();
    }

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
        if (_virtualRefreshPending)
        {
            _virtualRefreshQueued = true;
            return;
        }
        _virtualRefreshPending = true;
        RefreshVirtualDriversButton.IsEnabled = false;
        InstallVirtualInputButton.IsEnabled = false;
        InstallVirtualOutputButton.IsEnabled = false;
        VirtualDriverSummary.Text = "Searching installed AuralWeave-family PnP devices...";
        try
        {
            // PnP enumeration and Authenticode signature validation may
            // block, including while Windows checks certificate revocation.
            // Keep all of that work off the WPF dispatcher/UI thread.
            var scan = await Task.Run(() =>
            {
                var devices = AuralWeaveVirtualDrivers.EnumerateInstalled();
                var input = VirtualDriverInstallation.Check(
                    devices, VirtualAudioEndpointRole.Input);
                var output = VirtualDriverInstallation.Check(
                    devices, VirtualAudioEndpointRole.Output);
                return (Devices: devices, Input: input, Output: output);
            });
            var installed = scan.Devices;
            var inputDevices = installed.Where(
                x => x.Role == VirtualAudioEndpointRole.Input).ToArray();
            var outputDevices = installed.Where(
                x => x.Role == VirtualAudioEndpointRole.Output).ToArray();
            FillVirtualDeviceCard(VirtualInputDevices, inputDevices,
                "No AuralWeave Virtual Input installed (0/1).");
            FillVirtualDeviceCard(VirtualOutputDevices, outputDevices,
                "No AuralWeave Virtual Output installed (0/1).");

            var activeInput = inputDevices.Count(x => x.IsOperational);
            var activeOutput = outputDevices.Count(x => x.IsOperational);
            var legacy = installed.Count(x => x.IsLegacy);
            var inputReadiness = scan.Input;
            var outputReadiness = scan.Output;
            VirtualInputPackageStatus.Text = inputReadiness.Description;
            VirtualOutputPackageStatus.Text = outputReadiness.Description;
            // The UI offers INSTALL only if the Windows PnP ID is absent.
            // The signed production package must also pass trust preflight
            // before enabling the button. No install of unsigned WDK demos.
            InstallVirtualInputButton.Visibility =
                inputDevices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            InstallVirtualOutputButton.Visibility =
                outputDevices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var canOfferInstall = !_virtualInstallPending && !_virtualRefreshQueued;
            InstallVirtualInputButton.IsEnabled = canOfferInstall && inputReadiness.CanInstall;
            InstallVirtualOutputButton.IsEnabled = canOfferInstall && outputReadiness.CanInstall;

            VirtualDriverSummary.Text =
                $"Virtual Input {activeInput}/1 | Virtual Output {activeOutput}/1. " +
                (legacy > 0 ? $"{legacy} legacy PnP device(s) detected. " : "") +
                "Windows Plug and Play is shared across portable folders. " +
                "To uninstall a driver, use Windows Device Manager. " +
                "Only complete, trusted production driver packages are installable.";
        }
        catch (Exception ex)
        {
            VirtualDriverSummary.Text = "PnP enumeration failed: " + ex.Message;
            InstallVirtualInputButton.IsEnabled = false;
            InstallVirtualOutputButton.IsEnabled = false;
            Status("Virtual device discovery failed: " + ex.Message, true);
        }
        finally
        {
            _virtualRefreshPending = false;
            RefreshVirtualDriversButton.IsEnabled = true;
            if (_virtualRefreshQueued)
            {
                _virtualRefreshQueued = false;
                QueueVirtualDriverRefresh();
            }
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
            // Read-only list. Windows Device Manager owns driver removal.
            // No administrator /uninstall actions are exposed by AuralWeave.
            var row = new Border
            {
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(11)
            };
            row.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
            row.SetResourceReference(Border.BackgroundProperty, "SubtleBackground");
            var details = new StackPanel();
            details.Children.Add(new TextBlock
            {
                Text = $"{device.Name} ({device.InstallationStatus})",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            var identity = MutedText(device.InstanceId);
            identity.FontSize = 10;
            identity.ToolTip = device.HardwareId;
            details.Children.Add(identity);
            row.Child = details;
            panel.Children.Add(row);
        }
    }

    private async void InstallVirtualInputClicked(object sender, RoutedEventArgs e) =>
        await InstallVirtualDeviceAsync(VirtualAudioEndpointRole.Input);

    private async void InstallVirtualOutputClicked(object sender, RoutedEventArgs e) =>
        await InstallVirtualDeviceAsync(VirtualAudioEndpointRole.Output);

    private async Task InstallVirtualDeviceAsync(VirtualAudioEndpointRole role)
    {
        if (_virtualInstallPending) return;
        _virtualInstallPending = true;
        InstallVirtualInputButton.IsEnabled = false;
        InstallVirtualOutputButton.IsEnabled = false;
        try
        {
            var eligibility = await Task.Run(() =>
                VirtualDriverInstallation.Check(
                    AuralWeaveVirtualDrivers.EnumerateInstalled(), role));
            if (!eligibility.CanInstall)
            {
                Status(eligibility.Description, true);
                return;
            }

            var confirmation = MessageBox.Show(this,
                $"Install the AuralWeave Virtual {role} Windows audio driver?\n\n" +
                "This installs one signed driver device for the entire PC, " +
                "shared by every Portable copy. Windows will request administrator approval. " +
                "You can later remove the device using Windows Device Manager.",
                "AuralWeave - Install Virtual Driver",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) return;

            // Kernel deployment and UAC may block; never wait on the UI thread.
            var installedSuccessfully = await Task.Run(
                () => VirtualDriverInstallation.InstallAsync(role));
            Status(installedSuccessfully
                ? $"Virtual {role} installed. Device is shared across all AuralWeave copies."
                : $"Installer completed, but Windows did not report Virtual {role}. Check Device Manager.",
                !installedSuccessfully);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Status("Windows administrator approval was cancelled.");
        }
        catch (Exception ex)
        {
            Status($"Virtual {role} installation failed: {ex.Message}", true);
        }
        finally
        {
            _virtualInstallPending = false;
            await RefreshVirtualDriversAsync();
        }
    }
}
