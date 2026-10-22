using System;
using System.IO;
using System.Collections.Generic;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;


namespace QMK_Toolbox.Views;

public partial class MainWindow : Window, IWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnInitialized;
        Closing += (_, e) =>
        {
            if (((App)App.Current).MainWindowViewModel?.IsBusy == true)
            {
                e.Cancel = true;
                ShowMessage("Wait for the current device operation to finish before closing.");
            }
        };
        Closed += (_, _) => ((App)App.Current).MainWindowViewModel?.Dispose();
        
        
        AddHandler(DragDrop.DropEvent, Drop);
        AddHandler(DragDrop.DragOverEvent, DragOver);
    }

    private void OnInitialized(object sender, EventArgs e)
    {
        ((App)App.Current).MainWindowViewModel.InitUi();
    }

    private async void SaveLog(object sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            { Title = "Save QMK log", SuggestedFileName = "qmk-toolbox.log" });
        if (file == null) return;
        try
        {
            await using var output = await file.OpenWriteAsync();
            output.SetLength(0);
            await using var writer = new StreamWriter(output);
            await writer.WriteAsync(((App)App.Current).MainWindowViewModel.Log);
        }
        catch (Exception ex) { ShowMessage($"Could not save log: {ex.Message}"); }
    }

    private void OpenReleases(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("https://github.com/qmk/qmk_toolbox/releases") { UseShellExecute = true });

    private void ShowAbout(object sender, RoutedEventArgs e) => ShowMessage(
        "QMK Toolbox Linux\nFirmware flashing and integrated HID console\nBased on QMK Toolbox (MIT license)\nhttps://qmk.fm/toolbox");

    private void ShowKeyTester(object sender, RoutedEventArgs e)
    {
        var pressed = new HashSet<Key>();
        var text = new TextBlock { Text = "Press keys in this window.",
            Margin = new Avalonia.Thickness(20), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var window = new Window { Title = "QMK Key Tester", Width = 640, Height = 300, Content = text };
        window.KeyDown += (_, key) => { pressed.Add(key.Key); text.Text = "Pressed: " + string.Join(", ", pressed); key.Handled = true; };
        window.KeyUp += (_, key) => { pressed.Remove(key.Key); text.Text = $"Released: {key.Key}\nPressed: " + string.Join(", ", pressed); key.Handled = true; };
        window.Deactivated += (_, _) => { pressed.Clear(); text.Text = "Click this window to test keys."; };
        window.Show(this);
    }

    public void ShowMessage(string message)
    {
        var dialog = new Window
        {
            Title = "QMK Toolbox", Width = 480, Height = 180,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(20) }
        };
        _ = dialog.ShowDialog(this);
    }

    public void OnClose()
    {
        Close();
    }

    public async Task OnFileOpen()
    {
        var vm = ((App)App.Current).MainWindowViewModel;
        var hexFile = await GetPath();
        if (!string.IsNullOrEmpty(hexFile)) vm.HexFile = hexFile;
    }
    
    private async Task<string> GetPath()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Select QMK firmware", AllowMultiple = false,
            FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Firmware")
                { Patterns = new[] { "*.hex", "*.bin", "*.dfu" } } }
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        e.DragEffects = !((App)App.Current).MainWindowViewModel.IsBusy &&
            (e.DataTransfer.Contains(DataFormat.File) || e.DataTransfer.Contains(DataFormat.Text))
            ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void Drop(object sender, DragEventArgs e)
    {
        var vm = ((App)App.Current).MainWindowViewModel;
        if (vm.IsBusy) return;
        var path = e.DataTransfer.TryGetFile()?.TryGetLocalPath() ?? e.DataTransfer.TryGetText();
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
        if (!string.IsNullOrWhiteSpace(path)) vm.HexFile = path;
    }
}
