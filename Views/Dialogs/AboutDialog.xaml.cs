using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace WinBit.Views.Dialogs;

public sealed partial class AboutDialog : ContentDialog
{
    public string VersionText { get; }
    public string CopyrightText { get; }

    public AboutDialog()
    {
        InitializeComponent();
        VersionText = $"Version {ResolveVersion()}";
        CopyrightText = $"© {DateTime.Now.Year} WinBit contributors. Released under the MIT License.";
    }

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AboutDialog).Assembly;
        var informational = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
