using Microsoft.UI.Xaml.Data;

namespace WinBit.Infrastructure.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is long bytes ? FormatBytes(bytes) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0)
            return string.Empty;

        const long kb = 1024L;
        const long mb = 1024L * kb;
        const long gb = 1024L * mb;
        const long tb = 1024L * gb;

        return bytes switch
        {
            >= tb => $"{bytes / (double)tb:F1} TB",
            >= gb => $"{bytes / (double)gb:F1} GB",
            >= mb => $"{bytes / (double)mb:F1} MB",
            >= kb => $"{bytes / (double)kb:F0} KB",
            _     => $"{bytes} B",
        };
    }
}
