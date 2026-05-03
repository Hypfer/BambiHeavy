using Avalonia.Controls;
using BambiHeavy.Models;
using BambiHeavy.ViewModels;

namespace BambiHeavy.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnZoneSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (comboBox.DataContext is not LightMapping light) return;
        if (DataContext is not SettingsViewModel vm) return;

        var zone = (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (!string.IsNullOrEmpty(zone)) vm.AssignToZone(light, zone);
    }
}