using Microsoft.UI.Xaml.Data;
using WinBit.Core.BitTorrent;

namespace WinBit.Infrastructure.Converters;

public sealed class FileDownloadPriorityToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is FileDownloadPriority p ? ToDisplayString(p) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static string ToDisplayString(FileDownloadPriority priority) =>
        priority switch
        {
            FileDownloadPriority.DoNotDownload => "Do Not Download",
            FileDownloadPriority.Low           => "Low",
            FileDownloadPriority.Normal        => "Normal",
            FileDownloadPriority.High          => "High",
            FileDownloadPriority.Maximum       => "Maximum",
            _                                  => priority.ToString(),
        };
}
