using System.Collections.Generic;
using System.Net;
using Microsoft.UI.Xaml.Controls;

namespace WinBit.Views.Dialogs;

public sealed partial class AddPeersDialog : ContentDialog
{
    public AddPeersDialog()
    {
        InitializeComponent();
        IsPrimaryButtonEnabled = false;
    }

    public IReadOnlyList<(string Ip, int Port)> ParsedPeers { get; private set; } = [];

    private void OnPeersChanged(object sender, TextChangedEventArgs e)
    {
        ParsedPeers = Parse(PeersBox.Text);
        IsPrimaryButtonEnabled = ParsedPeers.Count > 0;
    }

    private static IReadOnlyList<(string Ip, int Port)> Parse(string text)
    {
        var result = new List<(string, int)>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (IPEndPoint.TryParse(trimmed, out var ep))
                result.Add((ep.Address.ToString(), ep.Port));
        }
        return result;
    }
}
