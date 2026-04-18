using Microsoft.UI.Xaml.Controls;

namespace WinBit.Services;

public interface INavigationService
{
    void Initialize(Frame frame);
    bool NavigateTo(Type pageType, object? parameter = null);
}
