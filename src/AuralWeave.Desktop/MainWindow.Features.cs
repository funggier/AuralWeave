using System.IO;
using System.Windows;
using AuralWeave.Core;
using Microsoft.Win32;

namespace AuralWeave.Desktop;

public partial class MainWindow
{
    private readonly FilterPresetStore _presetStore = new(
        Path.Combine(AppContext.BaseDirectory, "presets", "filters"));

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

    private void LoadPresetIntoStudio(FilterPresetDocument preset, string action = "Loaded")
    {
        var restored = FilterPresetStore.Restore(preset);
        var restoredCards = RestoreVstCards(restored);
        try
        {
            _route?.ReplaceFilterChain(restored, LoadedVstPlugins(restoredCards));
        }
        catch
        {
            foreach (var card in restoredCards.Values) card.Plugin?.Dispose();
            throw;
        }

        CloseVstCards();
        foreach (var (id, card) in restoredCards) _vstCards.Add(id, card);
        _filterDebounceTimer.Stop();
        _filters.Clear();
        _filters.AddRange(restored);
        _collapsedFilters.Clear();
        _collapseAllNext = true;
        PresetNameBox.Text = preset.Name;
        RefreshFilterRows();
        var skipped = restored.Count < preset.Filters.Count
            ? " Pitch Shift was skipped." : string.Empty;
        var missing = preset.Filters.Count(filter => filter.VstPlugin is not null) - restoredCards.Count;
        var pending = restoredCards.Values.Count(card => card.Plugin is null);
        var unavailable = missing > 0 ? $" {missing} VST plugin(s) unavailable." : "";
        var manual = pending > 0 ? $" Click Load Plugin on {pending} VST card(s)." : "";
        Status($"{action} '{preset.Name}' · {restored.Count} filter(s).{skipped}{unavailable}{manual}");
        foreach (var stage in restored) StartCommonVstScan(stage);
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
            LoadPresetIntoStudio(preset, action: "Imported");
            _presetStore.Save(preset, overwrite);
            RefreshPresetList(preset.Name);
        }
        catch (Exception ex)
        {
            Status("Cannot import Filter preset: " + ex.Message, true);
        }
    }
}
