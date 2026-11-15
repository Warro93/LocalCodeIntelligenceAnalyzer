using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Lcia.App.Services;

public interface IDialogService
{
    string? PickFolder(string title, string? initialDirectory = null);

    string? PickFile(string title, string filter, string? initialDirectory = null);

    void ShowInfo(string title, string message);

    void ShowError(string title, string message);

    bool Confirm(string title, string message);

    /// <summary>Apre una cartella in Esplora file (sola visualizzazione).</summary>
    void OpenFolder(string path);

    /// <summary>Apre Esplora file selezionando il file indicato.</summary>
    void RevealFile(string path);

    bool TrySetClipboard(string text);
}

/// <summary>Dialoghi nativi di Windows (OpenFolderDialog di WPF per .NET 8+).</summary>
public sealed class DialogService : IDialogService
{
    private static Window? Owner => System.Windows.Application.Current?.MainWindow;

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var accepted = Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string? initialDirectory = null)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var accepted = Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        return accepted == true ? dialog.FileName : null;
    }

    public void ShowInfo(string title, string message) => Show(title, message, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string title, string message) => Show(title, message, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string title, string message) =>
        Show(title, message, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void OpenFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
    }

    public void RevealFile(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
    }

    public bool TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static MessageBoxResult Show(string title, string message, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}
