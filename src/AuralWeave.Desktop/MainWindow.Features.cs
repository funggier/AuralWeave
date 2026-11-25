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
}
