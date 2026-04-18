using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinBit.Controls;

/// <summary>
/// Win2D-backed pieces visualization. Each <see cref="Pieces"/> entry is one segment — true is
/// "have", false is "missing". Colors resolve from theme brushes so light/dark divergence is
/// free; <c>ActualThemeChanged</c> triggers a redraw so brush swaps land immediately.
/// </summary>
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
        var haveColor = ResolveColor("AccentFillColorDefaultBrush");
        var missingColor = ResolveColor("ControlFillColorDefaultBrush");

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

    private static Color ResolveColor(string resourceKey)
    {
        if (Application.Current.Resources[resourceKey] is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Color.FromArgb(0, 0, 0, 0);
    }
}
