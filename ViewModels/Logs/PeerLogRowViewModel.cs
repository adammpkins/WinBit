using System.Globalization;
using WinBit.Core.Logging;

namespace WinBit.ViewModels.Logs;

public sealed class PeerLogRowViewModel
{
    public PeerLogRowViewModel(PeerLogEntry entry)
    {
        Entry = entry;
        TimestampText = entry.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public PeerLogEntry Entry { get; }

    public string TimestampText { get; }

    public string PeerEndpoint => Entry.PeerEndpoint;

    public string Reason => Entry.Reason;
}
