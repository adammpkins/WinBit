using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinBit.Core.BitTorrent;

namespace WinBit.Views.TorrentCreator;

public sealed partial class TorrentCreatorPage : Page
{
    private readonly ITorrentCreatorService _creator;
    private CancellationTokenSource? _cts;

    public TorrentCreatorPage()
    {
        InitializeComponent();
        _creator = App.Services.GetRequiredService<ITorrentCreatorService>();
    }

    private static nint OwnerHwnd =>
        App.MainWindow is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    private async void OnBrowseSourceFileClicked(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null) return;
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            SourceBox.Text = file.Path;
            SuggestOutputPath(file.Path, isDirectory: false);
        }
    }

    private async void OnBrowseSourceFolderClicked(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null) return;
        var picker = new FolderPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            SourceBox.Text = folder.Path;
            SuggestOutputPath(folder.Path, isDirectory: true);
        }
    }

    private async void OnBrowseOutputClicked(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is null) return;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SuggestFileName(),
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, OwnerHwnd);
        picker.FileTypeChoices.Add("Torrent", new List<string> { ".torrent" });
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            OutputBox.Text = file.Path;
        }
    }

    private string SuggestFileName()
    {
        var src = SourceBox.Text;
        if (string.IsNullOrWhiteSpace(src))
        {
            return "new.torrent";
        }
        var name = Directory.Exists(src) ? new DirectoryInfo(src).Name : Path.GetFileNameWithoutExtension(src);
        return string.IsNullOrWhiteSpace(name) ? "new.torrent" : name + ".torrent";
    }

    private void SuggestOutputPath(string source, bool isDirectory)
    {
        if (!string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            return;
        }
        var parent = isDirectory ? Path.GetDirectoryName(source) : Path.GetDirectoryName(source);
        var baseName = isDirectory ? new DirectoryInfo(source).Name : Path.GetFileNameWithoutExtension(source);
        if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(baseName))
        {
            OutputBox.Text = Path.Combine(parent, baseName + ".torrent");
        }
    }

    private async void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceBox.Text))
        {
            ShowStatus(InfoBarSeverity.Warning, "Pick a source file or folder first.", string.Empty);
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            ShowStatus(InfoBarSeverity.Warning, "Pick an output path first.", string.Empty);
            return;
        }

        var request = new TorrentCreateRequest
        {
            SourcePath = SourceBox.Text,
            OutputPath = OutputBox.Text,
            TrackerTiers = ParseTiers(TrackersBox.Text),
            WebSeeds = ParseLines(WebSeedsBox.Text),
            IsPrivate = PrivateToggle.IsOn,
            Comment = EmptyToNull(CommentBox.Text),
            CreatedBy = EmptyToNull(CreatedByBox.Text),
            PieceLength = ReadPieceLength(),
        };

        _cts = new CancellationTokenSource();
        CreateButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressBarControl.Value = 0;
        ProgressBarControl.Visibility = Visibility.Visible;
        ProgressText.Text = "Hashing…";
        StatusBar.IsOpen = false;

        var progress = new Progress<TorrentCreateProgress>(p =>
        {
            ProgressBarControl.Value = Math.Clamp(p.OverallCompletion, 0, 1);
            ProgressText.Text = $"{p.OverallCompletion * 100:F0}% — {Path.GetFileName(p.CurrentFile)}";
        });

        var result = await _creator.CreateAsync(request, progress, _cts.Token);

        ProgressBarControl.Visibility = Visibility.Collapsed;
        ProgressText.Text = string.Empty;
        CreateButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        _cts.Dispose();
        _cts = null;

        if (result.IsSuccess)
        {
            ShowStatus(InfoBarSeverity.Success, "Torrent created.", OutputBox.Text);
        }
        else
        {
            ShowStatus(InfoBarSeverity.Error, "Torrent creation failed.", result.Error ?? string.Empty);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private int? ReadPieceLength()
    {
        if (PieceSizeCombo.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var size) && size > 0)
        {
            return size;
        }
        return null;
    }

    private static List<string> ParseLines(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new();
        }
        return raw.Split('\n', StringSplitOptions.None)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static List<IReadOnlyList<string>> ParseTiers(string raw)
    {
        var tiers = new List<IReadOnlyList<string>>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return tiers;
        }

        var current = new List<string>();
        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    tiers.Add(current);
                    current = new();
                }
            }
            else
            {
                current.Add(line);
            }
        }
        if (current.Count > 0)
        {
            tiers.Add(current);
        }
        return tiers;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
