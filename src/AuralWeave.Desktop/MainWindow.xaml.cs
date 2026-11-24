using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using AuralWeave.Audio.Windows;
using AuralWeave.Core;

namespace AuralWeave.Desktop;

public partial class MainWindow : Window
{
    private sealed record FilterOption(FilterKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly FilterOption[] FilterOptions =
    [
        new(FilterKind.None, "Choose filter..."),
        new(FilterKind.Gain, "Gain"),
        new(FilterKind.RnnoiseStandard, "RNNoise Standard"),
        new(FilterKind.NoiseGate, "Noise Gate"),
        new(FilterKind.Limiter, "Limiter")
    ];

    private readonly List<FilterStage> _filters = [];
    private readonly HashSet<Guid> _collapsedFilters = [];
    private readonly List<OutputRoute> _outputs = [];
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _filterDebounceTimer;
    private IReadOnlyList<AudioEndpoint> _captureDevices = [];
    private IReadOnlyList<AudioEndpoint> _renderDevices = [];
    private NativeWasapiAudioRoute? _route;
    private bool _refreshingOutputs;
    private bool _refreshingFilters;
    private bool _loadingSkin = true;
    private bool _collapseAllNext = true;

    public MainWindow()
    {
        InitializeComponent();
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
        try { SkinManager.Apply(skin, true); }
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
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Output gain (0.00x - 2.00x)"
                };
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

    private void UpdateFilter(Guid id, FilterKind? kind = null, bool? enabled = null,
                              float? parameter = null, bool refresh = false)
    {
        var i = _filters.FindIndex(x => x.Id == id);
        if (i < 0) return;
        var item = _filters[i];
        var finalKind = kind ?? item.Kind;
        _filters[i] = item with
        {
            Kind = finalKind,
            Parameter = kind.HasValue
                ? FilterStage.DefaultParameter(finalKind) : parameter ?? item.Parameter,
            Enabled = enabled ?? item.Enabled
        };
        _filterDebounceTimer.Stop();
        if (refresh) RefreshFilterRows();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        try { _route?.ReplaceFilterChain(_filters); }
        catch (Exception ex) { Status("Filter chain: " + ex.Message, true); }
    }

    private void QueueFilterParameter(Guid id, float value)
    {
        var i = _filters.FindIndex(x => x.Id == id);
        if (i < 0) return;
        _filters[i] = _filters[i] with { Parameter = value };
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
                    SelectedItem = FilterOptions.First(x => x.Kind == filter.Kind),
                    Margin = new Thickness(0, 0, 12, 0),
                    ToolTip = "Choose the processing type for this filter slot"
                };
                Grid.SetColumn(choices, 1);
                choices.SelectionChanged += (_, _) =>
                {
                    if (_refreshingFilters || choices.SelectedItem is not FilterOption choice) return;
                    UpdateFilter(filter.Id, kind: choice.Kind, refresh: true);
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
                    RefreshFilterRows();
                }));
                Grid.SetColumn(actions, 3);
                header.Children.Add(actions);
                body.Children.Add(header);

                if (!collapsed)
                {
                    if (filter.Kind == FilterKind.None)
                    {
                        body.Children.Add(MutedText(
                            "Choose a filter type above. This empty slot does not process audio."));
                    }
                    else if (filter.Kind == FilterKind.RnnoiseStandard)
                    {
                        body.Children.Add(MutedText(
                            "RNNoise Standard · Xiph v0.1.1 · 48 kHz / 10 ms frames. " +
                            "Each instance has its own processing state."));
                    }
                    else
                    {
                        (double min, double max, string label, string suffix) = filter.Kind switch
                        {
                            FilterKind.Gain => (0, 2, "Gain level", "x"),
                            FilterKind.NoiseGate => (-80, 0, "Threshold · 5 ms attack / 80 ms release", "dB"),
                            FilterKind.Limiter => (-24, 0, "Ceiling · zero-lookahead peak limiting", "dB"),
                            _ => (0, 1, "Parameter", "")
                        };
                        var row = new Grid { Margin = new Thickness(8, 13, 8, 4) };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                        row.Children.Add(new TextBlock
                        {
                            Text = label, VerticalAlignment = VerticalAlignment.Center
                        });
                        var slider = new Slider
                        {
                            Minimum = min, Maximum = max, Value = filter.Parameter,
                            IsEnabled = filter.Enabled,
                            VerticalAlignment = VerticalAlignment.Center,
                            ToolTip = label
                        };
                        Grid.SetColumn(slider, 1);
                        row.Children.Add(slider);
                        var level = new TextBlock
                        {
                            Text = $"{filter.Parameter:0.00} {suffix}",
                            TextAlignment = TextAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        Grid.SetColumn(level, 2);
                        row.Children.Add(level);
                        slider.ValueChanged += (_, _) =>
                        {
                            level.Text = $"{slider.Value:0.00} {suffix}";
                            QueueFilterParameter(filter.Id, (float)slider.Value);
                        };
                        body.Children.Add(row);
                    }
                }
                card.Child = body;
                FiltersPanel.Children.Add(card);
            }
        }
        finally { _refreshingFilters = false; }
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
            candidate = new NativeWasapiAudioRoute();
            candidate.ReplaceFilterChain(_filters);
            candidate.Start(input.Id, _outputs);
            _route = candidate;
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
            FrameCountLabel.Text = "0 frames";
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
        FrameCountLabel.Text = $"{route.CapturedFrames:N0} frames";
        var overrun = route.DroppedRenderFrames;
        var underrun = route.UnderrunRenderFrames;
        var metrics = route.GetOutputTelemetry();
        OutputStatusLabel.Text =
            (overrun > 0 ? $"Buffer overruns: {overrun:N0}  ·  " : "") +
            (underrun > 0 ? $"Buffer underruns: {underrun:N0}  ·  " : "") +
            string.Join("  ·  ", metrics.Select((x, i) =>
                $"Output {i + 1}: Native WASAPI" + (x.Error is null ? "" : $" ERROR {x.Error}")));
        var error = route.LastError;
        if (!route.IsRunning)
        {
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
        base.OnClosing(e);
    }
}
