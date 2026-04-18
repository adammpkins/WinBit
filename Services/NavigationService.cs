using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace WinBit.Services;

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;

    public void Initialize(Frame frame) => _frame = frame;

    public bool NavigateTo(Type pageType, object? parameter = null)
    {
        if (_frame is null)
        {
            return false;
        }

        if (_frame.CurrentSourcePageType == pageType)
        {
            return true;
        }

        return _frame.Navigate(pageType, parameter, new DrillInNavigationTransitionInfo());
    }
}
