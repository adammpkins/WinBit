using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace WinBit.Controls;

public sealed partial class PiecesBar : UserControl
{
    public static readonly DependencyProperty PiecesProperty = DependencyProperty.Register(
        nameof(Pieces),
        typeof(IReadOnlyList<bool>),
        typeof(PiecesBar),
        new PropertyMetadata(Array.Empty<bool>(), OnPiecesChanged));

    public PiecesBar()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Canvas.Invalidate();
    }

    public IReadOnlyList<bool> Pieces
    {
        get => (IReadOnlyList<bool>)GetValue(PiecesProperty);
        set => SetValue(PiecesProperty, value);
    }

    private static void OnPiecesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PiecesBar)d).Canvas.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        // UISettings.GetColorValue always returns the live system accent color regardless of app theme.
        var haveColor = new UISettings().GetColorValue(UIColorType.Accent);
        // ControlFillColorDefaultBrush resolves to near-transparent in dark mode (#0FFFFFFF),
        // so we derive a visible neutral gray from ActualTheme instead.
        var missingColor = ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(255, 55, 55, 55)
            : Color.FromArgb(255, 200, 200, 200);

        var width = (float)sender.Size.Width;
        var height = (float)sender.Size.Height;
        var pieces = Pieces;

        if (pieces.Count == 0 || width <= 0 || height <= 0)
        {
            args.DrawingSession.FillRectangle(0, 0, width, height, missingColor);
            return;
        }

        var segmentWidth = width / pieces.Count;

        for (var i = 0; i < pieces.Count; i++)
        {
            var color = pieces[i] ? haveColor : missingColor;
            args.DrawingSession.FillRectangle(i * segmentWidth, 0, MathF.Max(segmentWidth, 1f), height, color);
        }
    }
}
