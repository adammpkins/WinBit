using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinBit.Controls;

/// <summary>
/// Win2D scrolling line chart. Two series (download / upload) plus a soft gradient fill
/// beneath each line. Colors resolve from theme brushes so light/dark divergence lands for
/// free; <c>ActualThemeChanged</c> triggers a redraw. Peak callouts are a polish follow-up —
/// this pass ships the lines + fills.
/// </summary>
public sealed partial class SpeedGraph : UserControl
{
    public static readonly DependencyProperty DownloadSamplesProperty = DependencyProperty.Register(
        nameof(DownloadSamples),
        typeof(IReadOnlyList<long>),
        typeof(SpeedGraph),
        new PropertyMetadata(Array.Empty<long>(), OnSamplesChanged));

    public static readonly DependencyProperty UploadSamplesProperty = DependencyProperty.Register(
        nameof(UploadSamples),
        typeof(IReadOnlyList<long>),
        typeof(SpeedGraph),
        new PropertyMetadata(Array.Empty<long>(), OnSamplesChanged));

    public SpeedGraph()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => Canvas.Invalidate();
    }

    public IReadOnlyList<long> DownloadSamples
    {
        get => (IReadOnlyList<long>)GetValue(DownloadSamplesProperty);
        set => SetValue(DownloadSamplesProperty, value);
    }

    public IReadOnlyList<long> UploadSamples
    {
        get => (IReadOnlyList<long>)GetValue(UploadSamplesProperty);
        set => SetValue(UploadSamplesProperty, value);
    }

    private static void OnSamplesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SpeedGraph)d).Canvas.Invalidate();

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var w = (float)sender.Size.Width;
        var h = (float)sender.Size.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        var downColor = ResolveColor("AccentFillColorDefaultBrush");
        var upColor = ResolveColor("SystemFillColorSuccessBrush");
        var gridColor = ResolveColor("DividerStrokeColorDefaultBrush");

        DrawGridlines(args.DrawingSession, w, h, gridColor);

        var maxValue = Math.Max(Max(DownloadSamples), Max(UploadSamples));
        if (maxValue <= 0)
        {
            return;
        }

        DrawSeries(args.DrawingSession, DownloadSamples, w, h, downColor, fillAlpha: 48, maxValue);
        DrawSeries(args.DrawingSession, UploadSamples, w, h, upColor, fillAlpha: 48, maxValue);

        DrawPeakCallout(args.DrawingSession, DownloadSamples, w, h, downColor, maxValue, yOffset: -12);
        DrawPeakCallout(args.DrawingSession, UploadSamples, w, h, upColor, maxValue, yOffset: 4);
    }

    private static void DrawPeakCallout(CanvasDrawingSession ds, IReadOnlyList<long> samples, float w, float h, Color color, long maxValue, float yOffset)
    {
        if (samples.Count == 0 || maxValue <= 0)
        {
            return;
        }

        var peakIndex = 0;
        var peakValue = samples[0];
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i] > peakValue)
            {
                peakValue = samples[i];
                peakIndex = i;
            }
        }

        if (peakValue <= 0)
        {
            return;
        }

        var step = w / Math.Max(samples.Count - 1, 1);
        var x = peakIndex * step;
        var y = h - (float)(peakValue / (double)maxValue) * h;
        var label = FormatSpeed(peakValue);

        using var format = new CanvasTextFormat
        {
            FontSize = 11,
            FontWeight = new Windows.UI.Text.FontWeight(600),
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top,
        };

        ds.FillCircle(x, Math.Clamp(y, 0, h), 2.5f, color);

        var textX = Math.Clamp(x, 24, w - 24);
        var textY = Math.Clamp(y + yOffset, 2, h - 16);
        ds.DrawText(label, textX, textY, color, format);
    }

    private static string FormatSpeed(long bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec} B/s";
        string[] units = { "KB/s", "MB/s", "GB/s", "TB/s" };
        double v = bytesPerSec;
        var u = -1;
        do { v /= 1024; u++; } while (v >= 1024 && u < units.Length - 1);
        return $"{v:0.#} {units[u]}";
    }

    private static void DrawGridlines(CanvasDrawingSession ds, float w, float h, Color color)
    {
        const int lines = 4;
        for (var i = 1; i < lines; i++)
        {
            var y = h * i / lines;
            ds.DrawLine(0, y, w, y, color, 0.5f);
        }
    }

    private static void DrawSeries(CanvasDrawingSession ds, IReadOnlyList<long> samples, float w, float h, Color color, byte fillAlpha, long maxValue)
    {
        if (samples.Count < 2)
        {
            return;
        }

        var step = w / (samples.Count - 1);

        using var path = new CanvasPathBuilder(ds);
        path.BeginFigure(0, h);
        for (var i = 0; i < samples.Count; i++)
        {
            var x = i * step;
            var y = h - (float)(samples[i] / (double)maxValue) * h;
            path.AddLine(x, Math.Clamp(y, 0, h));
        }
        path.AddLine((samples.Count - 1) * step, h);
        path.EndFigure(CanvasFigureLoop.Closed);

        using var fill = CanvasGeometry.CreatePath(path);
        ds.FillGeometry(fill, Color.FromArgb(fillAlpha, color.R, color.G, color.B));

        for (var i = 1; i < samples.Count; i++)
        {
            var x1 = (i - 1) * step;
            var y1 = h - (float)(samples[i - 1] / (double)maxValue) * h;
            var x2 = i * step;
            var y2 = h - (float)(samples[i] / (double)maxValue) * h;
            ds.DrawLine(x1, Math.Clamp(y1, 0, h), x2, Math.Clamp(y2, 0, h), color, 1.5f);
        }
    }

    private static long Max(IReadOnlyList<long> samples)
    {
        long max = 0;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i] > max)
            {
                max = samples[i];
            }
        }
        return max;
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
