using Microsoft.UI.Xaml.Controls;
using WinBit.Core.Sharing;

namespace WinBit.Views.Dialogs;

internal static class ShareLimitDialogHelpers
{
    public static ShareLimitAction ReadAction(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag is string tag && Enum.TryParse<ShareLimitAction>(tag, out var action)
            ? action
            : ShareLimitAction.Default;
}
