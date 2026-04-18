using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinBit.Services;

public sealed class DialogService : IDialogService
{
    private XamlRoot? _root;

    public void AttachRoot(XamlRoot root) => _root = root;

    public async Task ShowAsync(string title, string message)
    {
        if (_root is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = _root,
        };

        await dialog.ShowAsync();
    }
}
