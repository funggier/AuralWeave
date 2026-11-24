using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AuralWeave.Audio.Windows;
using AuralWeave.Core;

namespace AuralWeave.Desktop;

public partial class MainWindow : Window
{
    private readonly List<OutputRoute> _outputs = [];
    private readonly List<AudioEffectInstance> _effects = [];
    private readonly DispatcherTimer _meterTimer;
    private IReadOnlyList<AudioEndpoint> _captureDevices = [];
    private IReadOnlyList<AudioEndpoint> _renderDevices = [];
    private IAudioRouteSession? _route;
    private bool _updating;

    public MainWindow()
    {
        InitializeComponent();
        RefreshDevices();
        RefreshEffectRows();
        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();
    }

    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshDevices();

    private void RefreshDevices()
    {
        if (_route is not null) return;
        try
        {
            var oldInput = (InputCombo.SelectedItem as AudioEndpoint)?.Id;
            _captureDevices = WasapiDeviceCatalog.CaptureDevices();
            _renderDevices = WasapiDeviceCatalog.RenderDevices();
            InputCombo.ItemsSource = _captureDevices;
            InputCombo.SelectedItem = _captureDevices.FirstOrDefault(x => x.Id == oldInput)
                ?? _captureDevices.FirstOrDefault(x => x.IsDefault)
                ?? _captureDevices.FirstOrDefault();
            DeviceCountLabel.Text =
                $"{_captureDevices.Count} input/loopback sources · {_renderDevices.Count} outputs detected";
            RefreshOutputRows();
            Status("Devices refreshed. Route is stopped.");
        }
        catch (Exception ex) { Status("Device enumeration failed: " + ex.Message, true); }
    }

    private void AddOutputClicked(object sender, RoutedEventArgs e)
    {
        if (_route is not null || OutputChoiceCombo.SelectedItem is not AudioEndpoint device) return;
        _outputs.Add(new OutputRoute(Guid.NewGuid(), device.Id, Muted: true));
        RefreshOutputRows();
    }

    private void RefreshOutputRows()
    {
        if (_updating) return;
        _updating = true;
        try
        {
            OutputPanel.Children.Clear();
            var running = _route is not null;
            foreach (var (output, index) in _outputs.Select((x, i) => (x, i)).ToArray())
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                row.Children.Add(new TextBlock
                {
                    Text = $"{index + 1}.",
                    Width = 23,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var choices = _renderDevices
                    .Where(d => d.Id == output.EndpointId ||
                                !_outputs.Any(o => o.EndpointId == d.Id))
                    .ToList();
                var chosen = choices.FirstOrDefault(d => d.Id == output.EndpointId);
                if (chosen is null)
                {
                    chosen = new AudioEndpoint(output.EndpointId, "Disconnected: " + output.EndpointId,
                        AudioEndpointKind.Render);
                    choices.Insert(0, chosen);
                }
                var select = new ComboBox
                {
                    Width = 270,
                    DisplayMemberPath = nameof(AudioEndpoint.DisplayName),
                    ItemsSource = choices,
                    SelectedItem = chosen,
                    IsEnabled = !running
                };
                select.SelectionChanged += (_, _) =>
                {
                    if (_updating || select.SelectedItem is not AudioEndpoint replacement) return;
                    var idx = _outputs.FindIndex(o => o.Id == output.Id);
                    if (idx < 0) return;
                    if (_outputs.Any(o => o.Id != output.Id && o.EndpointId == replacement.Id)) return;
                    _outputs[idx] = _outputs[idx] with { EndpointId = replacement.Id };
                    RefreshOutputRows();
                };
                row.Children.Add(select);

                var mute = new CheckBox
                {
                    Content = "Mute",
                    Margin = new Thickness(12, 0, 7, 0),
                    IsChecked = output.Muted
                };
                mute.Checked += (_, _) => ChangeOutput(output.Id, null, true);
                mute.Unchecked += (_, _) => ChangeOutput(output.Id, null, false);
                row.Children.Add(mute);

                var volume = new Slider
                {
                    Minimum = 0, Maximum = 2,
                    Value = output.Gain, Width = 95,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Gain 0.00x to 2.00x"
                };
                var gainLabel = new TextBlock
                {
                    Width = 46, Margin = new Thickness(5, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = $"{output.Gain:0.00}x"
                };
                volume.ValueChanged += (_, _) =>
                {
                    gainLabel.Text = $"{volume.Value:0.00}x";
                    ChangeOutput(output.Id, (float)volume.Value, null);
                };
                row.Children.Add(volume);
                row.Children.Add(gainLabel);

                var remove = new Button { Content = "−", Width = 35, IsEnabled = !running };
                remove.Click += (_, _) =>
                {
                    _outputs.RemoveAll(o => o.Id == output.Id);
                    RefreshOutputRows();
                };
                row.Children.Add(remove);
                OutputPanel.Children.Add(row);
            }

            var choicesToAdd = AudioRouteRules.AvailableOutputs(_renderDevices, _outputs);
            var preferredId = (OutputChoiceCombo.SelectedItem as AudioEndpoint)?.Id;
            OutputChoiceCombo.ItemsSource = choicesToAdd;
            OutputChoiceCombo.SelectedItem = choicesToAdd.FirstOrDefault(x => x.Id == preferredId)
                ?? choicesToAdd.FirstOrDefault();
            OutputChoiceCombo.IsEnabled = choicesToAdd.Count != 0 && !running;
            AddOutputButton.IsEnabled = choicesToAdd.Count != 0 && !running;
            OutputHint.Text = choicesToAdd.Count == 0
                ? "Every available output is selected. Remove one to make it available again."
                : $"{choicesToAdd.Count} unselected output(s) available. New outputs start muted for safety; unmute explicitly.";
        }
        finally { _updating = false; }
    }

    private void ChangeOutput(Guid id, float? gain, bool? mute)
    {
        var index = _outputs.FindIndex(o => o.Id == id);
        if (index < 0) return;
        var current = _outputs[index];
        var updated = current with
        {
            Gain = gain ?? current.Gain,
            Muted = mute ?? current.Muted
        };
        _outputs[index] = updated;
        try { _route?.SetOutputLevel(id, updated.Gain, updated.Muted); }
        catch (Exception ex) { Status("Output control: " + ex.Message, true); }
    }

    private void AddEffectClicked(object sender, RoutedEventArgs e)
    {
        _effects.Add(new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect));
        ApplyEffects();
        RefreshEffectRows();
    }

    private void RefreshEffectRows()
    {
        EffectsPanel.Children.Clear();
        if (_effects.Count == 0)
        {
            EffectsPanel.Children.Add(new TextBlock
            {
                Text = "No effects — signal passes through unchanged.",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 4, 0, 4)
            });
            return;
        }

        foreach (var (effect, index) in _effects.Select((x, i) => (x, i)).ToArray())
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.Children.Add(new TextBlock
            {
                Text = $"{index + 1}. Gain",
                Width = 92,
                VerticalAlignment = VerticalAlignment.Center
            });
            var enabled = new CheckBox { Content = "On", Width = 58, IsChecked = effect.Enabled };
            enabled.Checked += (_, _) => ChangeEffect(effect.Id, true, null);
            enabled.Unchecked += (_, _) => ChangeEffect(effect.Id, false, null);
            row.Children.Add(enabled);

            var gainLabel = new TextBlock
            {
                Text = $"{effect.Gain:0.00}x",
                Width = 55, VerticalAlignment = VerticalAlignment.Center
            };
            var gain = new Slider
            {
                Minimum = 0, Maximum = 2, Value = effect.Gain,
                Width = 160, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Gain 0.00x to 2.00x"
            };
            gain.ValueChanged += (_, _) =>
            {
                gainLabel.Text = $"{gain.Value:0.00}x";
                ChangeEffect(effect.Id, null, (float)gain.Value);
            };
            row.Children.Add(gain);
            row.Children.Add(gainLabel);

            var up = new Button { Content = "↑", Width = 34, IsEnabled = index > 0 };
            up.Click += (_, _) => MoveEffect(effect.Id, -1);
            row.Children.Add(up);
            var down = new Button { Content = "↓", Width = 34, IsEnabled = index < _effects.Count - 1 };
            down.Click += (_, _) => MoveEffect(effect.Id, 1);
            row.Children.Add(down);
            var remove = new Button { Content = "−", Width = 34 };
            remove.Click += (_, _) =>
            {
                _effects.RemoveAll(x => x.Id == effect.Id);
                ApplyEffects();
                RefreshEffectRows();
            };
            row.Children.Add(remove);
            EffectsPanel.Children.Add(row);
        }
    }

    private void ChangeEffect(Guid id, bool? enabled, float? gain)
    {
        var index = _effects.FindIndex(x => x.Id == id);
        if (index < 0) return;
        var current = _effects[index];
        _effects[index] = current with
        {
            Enabled = enabled ?? current.Enabled,
            Gain = gain ?? current.Gain
        };
        ApplyEffects();
    }

    private void MoveEffect(Guid id, int direction)
    {
        var index = _effects.FindIndex(x => x.Id == id);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= _effects.Count) return;
        (_effects[index], _effects[next]) = (_effects[next], _effects[index]);
        ApplyEffects();
        RefreshEffectRows();
    }

    private void ApplyEffects()
    {
        try { _route?.ReplaceEffects(_effects); }
        catch (Exception ex) { Status("Effect configuration: " + ex.Message, true); }
    }

    private void StartStopClicked(object sender, RoutedEventArgs e)
    {
        if (_route is not null)
        {
            StopCurrentRoute();
            return;
        }
        StartCurrentRoute();
    }

    private void StartCurrentRoute()
    {
        if (_route is not null) return;
        if (InputCombo.SelectedItem is not AudioEndpoint input)
        {
            Status("Select an input source first.", true);
            return;
        }
        if (_outputs.Count == 0)
        {
            Status("Add at least one output device.", true);
            return;
        }

        IAudioRouteSession? candidate = null;
        try
        {
            candidate = NativeToggle.IsChecked == true
                ? new NativeWasapiAudioRoute() : new WasapiAudioRoute();
            candidate.ReplaceEffects(_effects);
            candidate.Start(input.Id, _outputs);
            _route = candidate;
            StartButton.Content = "Stop route";
            StartButton.Background = new SolidColorBrush(Color.FromRgb(214, 111, 98));
            StartButton.Foreground = Brushes.White;
            RefreshButton.IsEnabled = false;
            InputCombo.IsEnabled = false;
            NativeToggle.IsEnabled = false;
            RefreshOutputRows();
            Status($"Route started ({(candidate is NativeWasapiAudioRoute ? "Native C++" : "Managed WASAPI")}): {input.DisplayName} → {_outputs.Count} output(s).");
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
        try { _route.Dispose(); }
        catch (Exception ex) { Status("Stop warning: " + ex.Message, true); }
        finally
        {
            _route = null;
            StartButton.Content = "Start route";
            StartButton.Background = new SolidColorBrush(Color.FromRgb(112, 224, 173));
            StartButton.Foreground = new SolidColorBrush(Color.FromRgb(16, 38, 27));
            RefreshButton.IsEnabled = true;
            InputCombo.IsEnabled = true;
            NativeToggle.IsEnabled = NativeWasapiAudioRoute.Available;
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
        FrameCountLabel.Text = $"{route.CapturedFrames:N0} frames captured";
        var metrics = route.GetOutputTelemetry();
        var dropped = route.DroppedRenderFrames;
        var underruns = route.UnderrunRenderFrames;
        OutputStatusLabel.Text = (dropped > 0 ? $"Buffer overruns: {dropped:N0} | " : "")
            + (underruns > 0 ? $"Buffer underruns: {underruns:N0} | " : "") + string.Join("  |  ", metrics.Select((x, i) =>
            $"Output {i + 1}: {(double.IsNaN(x.BufferedMilliseconds) ? "native backend" : x.BufferedMilliseconds.ToString("0") + " ms buffered")}" +
            (x.Error is null ? "" : $" ERROR {x.Error}")));
        if (!string.IsNullOrEmpty(route.LastError))
            Status("Audio error: " + route.LastError, true);
        if (!route.IsRunning)
        {
            var err = route.LastError;
            StopCurrentRoute();
            if (!string.IsNullOrWhiteSpace(err))
                Status("Capture stopped: " + err, true);
        }
    }

    private void Status(string message, bool error = false)
    {
        StatusLabel.Text = message;
        StatusLabel.Foreground = error ? Brushes.LightCoral : Brushes.LightGreen;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _meterTimer.Stop();
        StopCurrentRoute();
        base.OnClosing(e);
    }
}
