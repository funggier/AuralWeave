using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using AuralWeave.Audio.Windows;
using AuralWeave.Core;

namespace AuralWeave.Desktop;

public partial class MainWindow : Window
{
    private sealed record FilterOption(FilterKind Kind, string Label, bool VstCommonFiles = false)
    {
        public override string ToString() => Label;
    }

    private static readonly FilterOption[] FilterOptions =
    [
        new(FilterKind.None, "Choose filter..."),
        new(FilterKind.Gain, "Gain"),
        new(FilterKind.RnnoiseStandard, "RNNoise Standard"),
        new(FilterKind.NoiseGate, "Noise Gate"),
        new(FilterKind.Limiter, "Limiter"),
        new(FilterKind.Equalizer, "EQ (3 Band)"),
        new(FilterKind.Equalizer5Band, "EQ (5 Band)"),
        new(FilterKind.Equalizer7Band, "EQ (7 Band)"),
        new(FilterKind.Compressor, "Compressor"),
        new(FilterKind.Vst2, "VST Plugin (Custom Location)"),
        new(FilterKind.Vst2, "VST Plugin (Common Files)", VstCommonFiles: true)
    ];

    private readonly List<FilterStage> _filters = [];
    private readonly HashSet<Guid> _collapsedFilters = [];
    private readonly List<OutputRoute> _outputs = [];
    private readonly WindowWorkAreaMaximizer _workAreaMaximizer;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _filterDebounceTimer;
    private IReadOnlyList<AudioEndpoint> _captureDevices = [];
    private IReadOnlyList<AudioEndpoint> _renderDevices = [];
    private NativeWasapiAudioRoute? _route;
    private long _captureRateMeasuredAt;
    private long _captureRateFrames;
    private string _outputStatus = "";
    private bool _refreshingOutputs;
    private bool _refreshingFilters;
    private bool _loadingSkin = true;
    private bool _collapseAllNext = true;

    public MainWindow()
    {
        InitializeComponent();
        _workAreaMaximizer = new WindowWorkAreaMaximizer(this);
        var skin = SkinManager.Load();
        SkinManager.Apply(skin, false);
        SkinCombo.SelectedIndex = skin == SkinManager.GraphiteSkin ? 1 : 0;
        _loadingSkin = false;
        StateChanged += (_, _) => UpdateWindowCaption();
        UpdateWindowCaption();
        _filterDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(125)
        };
        _filterDebounceTimer.Tick += (_, _) =>
        {
            _filterDebounceTimer.Stop();
            ApplyFilters();
        };
        _meterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        RefreshFilterRows();
        RefreshPresetList();
        RefreshDevices();
        _meterTimer.Start();
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { /* Drag gesture ended before drag began. */ }
    }

    private void TitleBarMouseRightButtonUp(object sender, MouseButtonEventArgs e) =>
        SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));

    private void MinimizeWindowClicked(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeWindowClicked(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseWindowClicked(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWindowCaption()
    {
        if (MaximizeWindowButton is not null)
            MaximizeWindowButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void SkinChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSkin || SkinCombo is null) return;
        var skin = SkinCombo.SelectedIndex == 1
            ? SkinManager.GraphiteSkin : SkinManager.DefaultSkin;
        try
        {
            SkinManager.Apply(skin, true);
            if (_vstCards.Values.Any(card => card.Plugin is not null))
                ApplyVstEditorPalette();
        }
        catch (Exception ex) { Status("Skin could not be changed: " + ex.Message, true); }
    }

    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshDevices();

    private void RefreshDevices()
    {
        if (_route is not null) return;
        try
        {
            var oldInputId = (InputCombo.SelectedItem as AudioEndpoint)?.Id;
            _captureDevices = NativeAudioDeviceCatalog.CaptureDevices();
            _renderDevices = NativeAudioDeviceCatalog.RenderDevices();
            InputCombo.ItemsSource = _captureDevices;
            InputCombo.SelectedItem = _captureDevices.FirstOrDefault(x => x.Id == oldInputId)
                ?? _captureDevices.FirstOrDefault(x => x.IsDefault && x.Kind == AudioEndpointKind.Capture)
                ?? _captureDevices.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
                ?? _captureDevices.FirstOrDefault();
            DeviceCountLabel.Text =
                $"{_captureDevices.Count} input/loopback sources  ·  {_renderDevices.Count} outputs detected";
            RefreshOutputRows();
            Status("Devices refreshed. Route is stopped.");
        }
        catch (Exception ex) { Status("Device enumeration failed: " + ex.Message, true); }
    }

    private void AddOutputClicked(object sender, RoutedEventArgs e)
    {
        if (_route is not null || OutputChoiceCombo.SelectedItem is not AudioEndpoint device) return;
        if (_outputs.Any(x => x.EndpointId == device.Id)) return;
        _outputs.Add(new OutputRoute(Guid.NewGuid(), device.Id, Muted: false));
        RefreshOutputRows();
    }

    private void RefreshOutputRows()
    {
        if (_refreshingOutputs) return;
        _refreshingOutputs = true;
        try
        {
            var running = _route is not null;
            OutputPanel.Children.Clear();
            foreach (var (output, index) in _outputs.Select((item, i) => (item, i)).ToArray())
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = $"{index + 1:00}",
                    VerticalAlignment = VerticalAlignment.Center
                });

                var choices = _renderDevices.Where(d => d.Id == output.EndpointId ||
                    !_outputs.Any(selected => selected.EndpointId == d.Id)).ToList();
                var chosen = choices.FirstOrDefault(d => d.Id == output.EndpointId);
                if (chosen is null)
                {
                    chosen = new AudioEndpoint(output.EndpointId, "Unavailable output",
                        AudioEndpointKind.Render);
                    choices.Insert(0, chosen);
                }
                var combo = new ComboBox
                {
                    DisplayMemberPath = nameof(AudioEndpoint.DisplayName),
                    ItemsSource = choices,
                    SelectedItem = chosen,
                    IsEnabled = !running,
                    Margin = new Thickness(0, 0, 9, 0),
                    ToolTip = chosen.DisplayName
                };
                Grid.SetColumn(combo, 1);
                combo.SelectionChanged += (_, _) =>
                {
                    if (_refreshingOutputs || combo.SelectedItem is not AudioEndpoint replacement) return;
                    var i = _outputs.FindIndex(x => x.Id == output.Id);
                    if (i < 0 || _outputs.Any(x => x.Id != output.Id &&
                                                x.EndpointId == replacement.Id)) return;
                    _outputs[i] = _outputs[i] with { EndpointId = replacement.Id };
                    RefreshOutputRows();
                };
                row.Children.Add(combo);

                var mute = new CheckBox
                {
                    Content = "Mute",
                    IsChecked = output.Muted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(mute, 2);
                mute.Checked += (_, _) => ChangeOutput(output.Id, null, true);
                mute.Unchecked += (_, _) => ChangeOutput(output.Id, null, false);
                row.Children.Add(mute);

                var slider = new Slider
                {
                    Minimum = 0, Maximum = 2, Value = output.Gain,
                    SmallChange = 0.01, LargeChange = 0.1,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Output gain (0.00x - 2.00x)"
                };
                EnableSliderWheel(slider);
                Grid.SetColumn(slider, 3);
                row.Children.Add(slider);
                var gainText = new TextBlock
                {
                    Text = $"{output.Gain:0.00}x",
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(gainText, 4);
                row.Children.Add(gainText);
                slider.ValueChanged += (_, _) =>
                {
                    gainText.Text = $"{slider.Value:0.00}x";
                    ChangeOutput(output.Id, (float)slider.Value, null);
                };
                var remove = MakeAction("−", "Remove output", !running, () =>
                {
                    _outputs.RemoveAll(x => x.Id == output.Id);
                    RefreshOutputRows();
                });
                Grid.SetColumn(remove, 5);
                row.Children.Add(remove);
                OutputPanel.Children.Add(row);
            }

            var available = AudioRouteRules.AvailableOutputs(_renderDevices, _outputs);
            var previous = (OutputChoiceCombo.SelectedItem as AudioEndpoint)?.Id;
            OutputChoiceCombo.ItemsSource = available;
            OutputChoiceCombo.SelectedItem = available.FirstOrDefault(x => x.Id == previous)
                ?? available.FirstOrDefault();
            OutputChoiceCombo.IsEnabled = available.Count > 0 && !running;
            AddOutputButton.IsEnabled = available.Count > 0 && !running;
            OutputHint.Text = available.Count == 0
                ? "All outputs are selected. Remove one to make it available again."
                : $"{available.Count} unused output(s) · new outputs start unmuted (use headphones / low gain)";
        }
        finally { _refreshingOutputs = false; }
    }

    private void ChangeOutput(Guid id, float? gain, bool? mute)
    {
        var i = _outputs.FindIndex(o => o.Id == id);
        if (i < 0) return;
        var item = _outputs[i];
        var updated = item with { Gain = gain ?? item.Gain, Muted = mute ?? item.Muted };
        _outputs[i] = updated;
        try { _route?.SetOutputLevel(id, updated.Gain, updated.Muted); }
        catch (Exception ex) { Status("Output settings: " + ex.Message, true); }
    }

    private void AddFilterClicked(object sender, RoutedEventArgs e)
    {
        if (_filters.Count >= 64)
        {
            Status("The filter chain supports up to 64 slots.", true);
            return;
        }
        _filters.Add(new FilterStage(Guid.NewGuid(), Enabled: true));
        RefreshFilterRows();
    }

    private void ToggleAllFiltersClicked(object sender, RoutedEventArgs e)
    {
        if (_collapseAllNext)
        {
            _collapsedFilters.Clear();
            foreach (var filter in _filters) _collapsedFilters.Add(filter.Id);
        }
        else _collapsedFilters.Clear();
        _collapseAllNext = !_collapseAllNext;
        RefreshFilterRows();
    }

    private void MoveFilter(Guid id, int direction)
    {
        var i = _filters.FindIndex(x => x.Id == id);
        var j = i + direction;
        if (i < 0 || j < 0 || j >= _filters.Count) return;
        (_filters[i], _filters[j]) = (_filters[j], _filters[i]);
        ApplyFilters();
        RefreshFilterRows();
    }

    private void UpdateFilter(Guid id, FilterOption? option = null, bool? enabled = null)
    {
        var i = _filters.FindIndex(x => x.Id == id);
        if (i < 0) return;
        var filter = _filters[i];
        var isEnabled = enabled ?? filter.Enabled;
        if (option is { } selected &&
            (selected.Kind != filter.Kind || selected.VstCommonFiles != filter.VstCommonFiles))
        {
            if (selected.Kind == filter.Kind)
            {
                // Changing the discovery source keeps the current effect playing.
                var card = GetVstCard(filter);
                _vstCards[id] = new VstCard(selected.VstCommonFiles) { Plugin = card.Plugin };
                _filters[i] = filter with
                {
                    VstCommonFiles = selected.VstCommonFiles,
                    VstPlugin = card.Plugin is null ? null : filter.VstPlugin
                };
            }
            else
            {
                _filters[i] = new FilterStage(id, selected.Kind, isEnabled,
                    FilterStage.DefaultParameter(selected.Kind), VstCommonFiles: selected.VstCommonFiles);
                _filterDebounceTimer.Stop();
                ApplyFilters();
                if (filter.Kind == FilterKind.Vst2) RemoveVstCard(id);
            }
            RefreshFilterRows();
            StartCommonVstScan(_filters[i]);
            return;
        }

        _filters[i] = filter with { Enabled = isEnabled };
        _filterDebounceTimer.Stop();
        ApplyFilters();
    }


    private void ApplyFilters()
    {
        try { if (_route is { } route) PublishFilterChain(route); }
        catch (Exception ex) { Status("Filter chain: " + ex.Message, true); }
    }

    private void QueueFilterSettings(Guid id, Func<FilterStage, FilterStage> change)
    {
        var i = _filters.FindIndex(x => x.Id == id);
        if (i < 0) return;
        _filters[i] = change(_filters[i]);
        // Keep slider dragging responsive without rebuilding an RNNoise graph
        // for every pixel of movement. DSP parameters commit after idle.
        if (_route is null) return;
        _filterDebounceTimer.Stop();
        _filterDebounceTimer.Start();
    }

    private static Button MakeAction(string text, string description,
                                     bool enabled, Action action)
    {
        var button = new Button
        {
            Content = text,
            ToolTip = description,
            Width = 34,
            MinHeight = 31,
            Padding = new Thickness(0),
            IsEnabled = enabled
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void RefreshFilterRows()
    {
        if (_refreshingFilters) return;
        _refreshingFilters = true;
        foreach (var card in _vstCards.Values) card.SettingsPanel = null;
        try
        {
            FiltersPanel.Children.Clear();
            FilterCountLabel.Text = _filters.Count == 0
                ? "· direct path" : $"· {_filters.Count} slot(s)";
            GlobalFilterExpandButton.IsEnabled = _filters.Count > 0;
            GlobalFilterExpandButton.Content = _collapseAllNext ? "Collapse all" : "Expand all";
            FilterHelpText.Visibility = _filters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var (filter, index) in _filters.Select((f, i) => (f, i)).ToArray())
            {
                var collapsed = _collapsedFilters.Contains(filter.Id);
                var card = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(13),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 9)
                };
                card.SetResourceReference(Border.BackgroundProperty, "SubtleBackground");
                card.SetResourceReference(Border.BorderBrushProperty, "PanelStroke");
                var body = new StackPanel();
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(new TextBlock
                {
                    Text = $"{index + 1:00}",
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var choices = new ComboBox
                {
                    ItemsSource = FilterOptions,
                    DisplayMemberPath = nameof(FilterOption.Label),
                    SelectedItem = FilterOptions.First(x =>
                        x.Kind == filter.Kind && x.VstCommonFiles == filter.VstCommonFiles),
                    Margin = new Thickness(0, 0, 12, 0),
                    ToolTip = "Choose the processing type for this filter slot"
                };
                Grid.SetColumn(choices, 1);
                choices.SelectionChanged += (_, _) =>
                {
                    if (_refreshingFilters || choices.SelectedItem is not FilterOption choice) return;
                    UpdateFilter(filter.Id, option: choice);
                };
                header.Children.Add(choices);
                var toggle = new CheckBox
                {
                    Content = "On",
                    IsEnabled = filter.Kind != FilterKind.None,
                    IsChecked = filter.Enabled,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(toggle, 2);
                toggle.Checked += (_, _) => UpdateFilter(filter.Id, enabled: true);
                toggle.Unchecked += (_, _) => UpdateFilter(filter.Id, enabled: false);
                header.Children.Add(toggle);

                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(MakeAction("↑", "Move filter up", index > 0,
                    () => MoveFilter(filter.Id, -1)));
                actions.Children.Add(MakeAction("↓", "Move filter down",
                    index < _filters.Count - 1, () => MoveFilter(filter.Id, 1)));
                actions.Children.Add(MakeAction(collapsed ? "⌄" : "⌃",
                    collapsed ? "Expand settings" : "Collapse settings", true, () =>
                    {
                        if (!_collapsedFilters.Add(filter.Id))
                            _collapsedFilters.Remove(filter.Id);
                        RefreshFilterRows();
                    }));
                actions.Children.Add(MakeAction("−", "Remove filter", true, () =>
                {
                    _filters.RemoveAll(x => x.Id == filter.Id);
                    _collapsedFilters.Remove(filter.Id);
                    ApplyFilters();
                    RemoveVstCard(filter.Id);
                    RefreshFilterRows();
                }));
                Grid.SetColumn(actions, 3);
                header.Children.Add(actions);
                body.Children.Add(header);

                if (!collapsed)
                {
                    var settings = new StackPanel();
                    settings.SetBinding(IsEnabledProperty, new Binding(nameof(CheckBox.IsChecked))
                    {
                        Source = toggle
                    });
                    BuildFilterSettings(settings, filter);
                    body.Children.Add(settings);
                }
                card.Child = body;
                FiltersPanel.Children.Add(card);
            }
        }
        finally { _refreshingFilters = false; }
    }

    private void BuildFilterSettings(Panel panel, FilterStage filter)
    {
        void Slider(string label, float value, double min, double max, double step,
                    string unit, Func<FilterStage, float, FilterStage> change) =>
            panel.Children.Add(MakeFilterSlider(label, value, min, max, step, unit,
                updated => QueueFilterSettings(filter.Id, stage => change(stage, updated))));

        switch (filter.Kind)
        {
            case FilterKind.None:
                panel.Children.Add(MutedText("Choose a filter type above."));
                break;
            case FilterKind.Vst2:
                BuildVstSettings(panel, filter.Id, GetVstCard(filter));
                break;
            case FilterKind.RnnoiseStandard:
                panel.Children.Add(MutedText("RNNoise Standard · Xiph v0.1.1 · 48 kHz / 10 ms frames"));
                break;
            case FilterKind.Gain:
                Slider("Gain level", filter.Parameter, 0, 2, 0.01, "x",
                    (stage, value) => stage with { Parameter = value });
                break;
            case FilterKind.NoiseGate:
                Slider("Threshold", filter.Parameter, -80, 0, 0.5, "dBFS",
                    (stage, value) => stage with { Parameter = value });
                break;
            case FilterKind.Limiter:
                Slider("Ceiling", filter.Parameter, -24, 0, 0.1, "dBFS",
                    (stage, value) => stage with { Parameter = value });
                break;
            case FilterKind.Equalizer:
                var eq = filter.Equalizer ?? EqualizerSettings.Flat;
                Slider("Low shelf · 160 Hz", eq.LowDb, -12, 12, 0.1, "dB",
                    (stage, value) => stage with
                    { Equalizer = (stage.Equalizer ?? EqualizerSettings.Flat) with { LowDb = value } });
                Slider("Mid bell · 1 kHz", eq.MidDb, -12, 12, 0.1, "dB",
                    (stage, value) => stage with
                    { Equalizer = (stage.Equalizer ?? EqualizerSettings.Flat) with { MidDb = value } });
                Slider("High shelf · 3.5 kHz", eq.HighDb, -12, 12, 0.1, "dB",
                    (stage, value) => stage with
                    { Equalizer = (stage.Equalizer ?? EqualizerSettings.Flat) with { HighDb = value } });
                break;
            case FilterKind.Equalizer5Band:
            case FilterKind.Equalizer7Band:
                string[] labels = filter.Kind == FilterKind.Equalizer5Band
                    ? ["Low shelf · 100 Hz", "300 Hz", "1 kHz", "3 kHz", "High shelf · 8 kHz"]
                    : ["Low shelf · 80 Hz", "160 Hz", "400 Hz", "1 kHz", "2.5 kHz", "5 kHz", "High shelf · 10 kHz"];
                var flat = ImmutableArray.CreateRange(new float[labels.Length]);
                var gains = filter.BandGainsDb ?? flat;
                for (var i = 0; i < labels.Length; ++i)
                {
                    var band = i;
                    Slider(labels[band], gains[band], -12, 12, 0.1, "dB",
                        (stage, value) => stage with
                        { BandGainsDb = (stage.BandGainsDb ?? flat).SetItem(band, value) });
                }
                break;
            case FilterKind.Compressor:
                var comp = filter.Compressor ?? CompressorSettings.Default;
                Slider("Threshold", comp.ThresholdDb, -60, 0, 0.5, "dBFS",
                    (stage, value) => stage with
                    { Compressor = (stage.Compressor ?? CompressorSettings.Default) with { ThresholdDb = value } });
                Slider("Ratio", comp.Ratio, 1, 20, 0.1, ":1",
                    (stage, value) => stage with
                    { Compressor = (stage.Compressor ?? CompressorSettings.Default) with { Ratio = value } });
                Slider("Attack", comp.AttackMs, 1, 100, 1, "ms",
                    (stage, value) => stage with
                    { Compressor = (stage.Compressor ?? CompressorSettings.Default) with { AttackMs = value } });
                Slider("Release", comp.ReleaseMs, 10, 1000, 5, "ms",
                    (stage, value) => stage with
                    { Compressor = (stage.Compressor ?? CompressorSettings.Default) with { ReleaseMs = value } });
                Slider("Makeup gain", comp.MakeupDb, 0, 24, 0.1, "dB",
                    (stage, value) => stage with
                    { Compressor = (stage.Compressor ?? CompressorSettings.Default) with { MakeupDb = value } });
                break;
        }
    }

    private static Grid MakeFilterSlider(string label, float value, double min, double max,
                                         double step, string unit, Action<float> changed)
    {
        var row = new Grid { Margin = new Thickness(8, 8, 8, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(166) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        row.Children.Add(new TextBlock
        {
            Text = label, VerticalAlignment = VerticalAlignment.Center
        });

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = step,
            SmallChange = step,
            LargeChange = step * 10,
            IsSnapToTickEnabled = true,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = label
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, label);
        EnableSliderWheel(slider);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);

        string Format(double amount) => unit == ":1" ? $"{amount:0.##}:1" : $"{amount:0.##} {unit}";
        var level = new TextBlock
        {
            Text = Format(slider.Value),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(level, 2);
        row.Children.Add(level);
        slider.ValueChanged += (_, e) =>
        {
            level.Text = Format(e.NewValue);
            changed((float)e.NewValue);
        };
        return row;
    }

    private static void EnableSliderWheel(Slider slider)
    {
        var remainder = 0;
        slider.MouseLeave += (_, _) => remainder = 0;
        slider.PreviewMouseWheel += (_, e) =>
        {
            if (!slider.IsEnabled) return;
            e.Handled = true;
            remainder += e.Delta;
            var steps = remainder / Mouse.MouseWheelDeltaForOneLine;
            remainder %= Mouse.MouseWheelDeltaForOneLine;
            if (steps == 0) return;

            var value = Math.Round(slider.Value + steps * slider.SmallChange, 10);
            slider.SetCurrentValue(Slider.ValueProperty, Math.Clamp(value, slider.Minimum, slider.Maximum));
        };
    }

    private static TextBlock MutedText(string text)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 12, 8, 3),
            TextWrapping = TextWrapping.Wrap
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return block;
    }

    private void StartStopClicked(object sender, RoutedEventArgs e)
    {
        if (_route is not null) StopCurrentRoute();
        else StartCurrentRoute();
    }

    private void StartCurrentRoute()
    {
        if (_route is not null) return;
        if (InputCombo.SelectedItem is not AudioEndpoint input)
        {
            Status("Choose an input source first.", true);
            return;
        }
        if (_outputs.Count == 0)
        {
            Status("Add at least one output device.", true);
            return;
        }

        NativeWasapiAudioRoute? candidate = null;
        try
        {
            PrepareVstPlugins(input);
            candidate = new NativeWasapiAudioRoute();
            PublishFilterChain(candidate);
            candidate.Start(input.Id, _outputs);
            _route = candidate;
            _captureRateMeasuredAt = Stopwatch.GetTimestamp();
            _captureRateFrames = candidate.CapturedFrames;
            _outputStatus = string.Join("  ·  ", Enumerable.Range(1, _outputs.Count)
                .Select(i => $"Output {i}: Native WASAPI"));
            StartButton.Content = "STOP";
            StartButton.SetResourceReference(Control.BackgroundProperty, "StopButtonBrush");
            StartButton.SetResourceReference(Control.ForegroundProperty, "PrimaryText");
            RefreshButton.IsEnabled = false;
            InputCombo.IsEnabled = false;
            RefreshOutputRows();
            Status($"Native route started: {input.DisplayName} · {_outputs.Count} output(s)");
        }
        catch (Exception ex)
        {
            candidate?.Dispose();
            Status("Cannot start route: " + ex.Message, true);
        }
    }

    private void StopCurrentRoute()
    {
        if (_route is null) return;
        _filterDebounceTimer.Stop();
        try { _route.Dispose(); }
        catch (Exception ex) { Status("Stop warning: " + ex.Message, true); }
        finally
        {
            _route = null;
            StartButton.Content = "START";
            StartButton.SetResourceReference(Control.BackgroundProperty, "Accent");
            StartButton.SetResourceReference(Control.ForegroundProperty, "AccentText");
            RefreshButton.IsEnabled = true;
            InputCombo.IsEnabled = true;
            InputLevel.Value = 0;
            CaptureRateLabel.Text = "0 frames/s";
            OutputStatusLabel.Text = "";
            RefreshOutputRows();
            Status("Route stopped.");
        }
    }

    private void UpdateMeters()
    {
        var route = _route;
        if (route is null) return;
        InputLevel.Value = route.InputPeak * 100;
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_captureRateMeasuredAt, now).TotalSeconds;
        if (elapsed >= 1.0)
        {
            var captured = route.CapturedFrames;
            var framesPerSecond = Math.Max(0, captured - _captureRateFrames) / elapsed;
            CaptureRateLabel.Text = $"{framesPerSecond:N0} frames/s";
            _captureRateMeasuredAt = now;
            _captureRateFrames = captured;
        }
        var overrun = route.DroppedRenderFrames;
        var underrun = route.UnderrunRenderFrames;
        var running = route.IsRunning;
        OutputStatusLabel.Text =
            (overrun > 0 ? $"Buffer overruns: {overrun:N0}  ·  " : "") +
            (underrun > 0 ? $"Buffer underruns: {underrun:N0}  ·  " : "") +
            _outputStatus;
        if (!running)
        {
            var error = route.LastError;
            StopCurrentRoute();
            if (!string.IsNullOrWhiteSpace(error))
                Status("Audio stopped: " + error, true);
        }
    }

    private void Status(string message, bool error = false)
    {
        if (StatusLabel is null) return;
        StatusLabel.Text = message;
        StatusLabel.SetResourceReference(TextBlock.ForegroundProperty,
            error ? "ErrorText" : "Accent");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _meterTimer.Stop();
        _filterDebounceTimer.Stop();
        StopCurrentRoute();
        CloseVstCards();
        base.OnClosing(e);
    }
}
