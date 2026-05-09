using System;
using System.Windows;
using Firepit.Core.Settings;
using Firepit.Native;
using Microsoft.Win32;

namespace Firepit.Views;

public partial class SettingsDialog : Window
{
    private readonly ISettingsStore _store;
    private FirepitSettings _settings;

    public SettingsDialog(ISettingsStore store)
    {
        InitializeComponent();
        _store = store;
        _settings = store.Load();
        RootBox.Text = _settings.ProjectsRoot;
        SourceInitialized += (_, _) => WindowDarkMode.EnableForWindow(this);
    }

    public FirepitSettings? Result { get; private set; }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = RootBox.Text,
            Title = "Select projects root",
        };
        if (dialog.ShowDialog(this) == true)
        {
            RootBox.Text = dialog.FolderName;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var updated = _settings with { ProjectsRoot = RootBox.Text.Trim() };
        try
        {
            _store.Save(updated);
            Result = updated;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save settings: {ex.Message}", "Firepit",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnOpenJsonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(_store.SettingsPath))
            {
                _store.Save(_settings);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.SettingsPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open settings.json: {ex.Message}", "Firepit",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
