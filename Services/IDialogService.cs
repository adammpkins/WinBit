using Microsoft.UI.Xaml;

namespace WinBit.Services;

public interface IDialogService
{
    void AttachRoot(XamlRoot root);
    Task ShowAsync(string title, string message);
}
