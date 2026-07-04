using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using OpenFanControl.ViewModels;

namespace OpenFanControl.Views;

/// <summary>
/// Interactive fan-curve editor. Draws a temperature→duty graph with draggable points:
/// left-click empty space to add a point, drag a point to move it, right-click a point to
/// remove it. A live marker shows the current operating point. Edits mutate the bound
/// <see cref="Points"/> collection and fire <see cref="ChangedCommand"/> so the owner can persist.
/// </summary>
public sealed class CurveEditor : Control
{
    private const double TempMin = 20, TempMax = 110;
    private const double PctMin = 0, PctMax = 100;
    private const double HitRadius = 11;

    public static readonly StyledProperty<ObservableCollection<CurvePointViewModel>?> PointsProperty =
        AvaloniaProperty.Register<CurveEditor, ObservableCollection<CurvePointViewModel>?>(nameof(Points));

    public static readonly StyledProperty<ICommand?> ChangedCommandProperty =
        AvaloniaProperty.Register<CurveEditor, ICommand?>(nameof(ChangedCommand));

    public static readonly StyledProperty<double?> CurrentTemperatureProperty =
        AvaloniaProperty.Register<CurveEditor, double?>(nameof(CurrentTemperature));

    public static readonly StyledProperty<double?> CurrentPercentProperty =
        AvaloniaProperty.Register<CurveEditor, double?>(nameof(CurrentPercent));

    static CurveEditor()
    {
        AffectsRender<CurveEditor>(PointsProperty, CurrentTemperatureProperty, CurrentPercentProperty);
    }

    public CurveEditor()
    {
        // Re-render when the bound collection or any point changes.
        PointsProperty.Changed.AddClassHandler<CurveEditor>((c, e) => c.OnPointsChanged(e));
    }

    public ObservableCollection<CurvePointViewModel>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public ICommand? ChangedCommand
    {
        get => GetValue(ChangedCommandProperty);
        set => SetValue(ChangedCommandProperty, value);
    }

    public double? CurrentTemperature
    {
        get => GetValue(CurrentTemperatureProperty);
        set => SetValue(CurrentTemperatureProperty, value);
    }

    public double? CurrentPercent
    {
        get => GetValue(CurrentPercentProperty);
        set => SetValue(CurrentPercentProperty, value);
    }

    private int _dragIndex = -1;

    private void OnPointsChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.OldValue is ObservableCollection<CurvePointViewModel> oldC)
            oldC.CollectionChanged -= OnCollectionChanged;
        if (e.NewValue is ObservableCollection<CurvePointViewModel> newC)
            newC.CollectionChanged += OnCollectionChanged;
        InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    // ---- Geometry ----

    private Rect Plot()
    {
        double l = 34, t = 10, r = 12, b = 20;
        return new Rect(l, t, Math.Max(1, Bounds.Width - l - r), Math.Max(1, Bounds.Height - t - b));
    }

    private double X(Rect p, double temp) => p.X + (temp - TempMin) / (TempMax - TempMin) * p.Width;
    private double Y(Rect p, double pct) => p.Bottom - (pct - PctMin) / (PctMax - PctMin) * p.Height;
    private double TempFromX(Rect p, double x) => TempMin + (x - p.X) / p.Width * (TempMax - TempMin);
    private double PctFromY(Rect p, double y) => PctMin + (p.Bottom - y) / p.Height * (PctMax - PctMin);

    // ---- Pointer editing ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Points is null) return;

        var plot = Plot();
        var pos = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        int hit = FindPointNear(plot, pos);

        if (props.IsRightButtonPressed)
        {
            if (hit >= 0 && Points.Count > 2)
            {
                Points.RemoveAt(hit);
                Commit();
            }
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            if (hit >= 0)
            {
                _dragIndex = hit;
            }
            else
            {
                var np = new CurvePointViewModel(
                    Math.Round(Math.Clamp(TempFromX(plot, pos.X), TempMin, TempMax)),
                    Math.Round(Math.Clamp(PctFromY(plot, pos.Y), PctMin, PctMax)));
                Points.Add(np);
                _dragIndex = Points.IndexOf(np);
            }

            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Points is null) return;

        var plot = Plot();
        var pos = e.GetPosition(this);

        if (_dragIndex >= 0 && _dragIndex < Points.Count)
        {
            var pt = Points[_dragIndex];
            pt.Temperature = Math.Clamp(TempFromX(plot, pos.X), TempMin, TempMax);
            pt.Percent = Math.Clamp(PctFromY(plot, pos.Y), PctMin, PctMax);
            InvalidateVisual();
        }
        else
        {
            Cursor = FindPointNear(plot, pos) >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragIndex < 0) return;

        if (_dragIndex < Points!.Count)
        {
            var pt = Points[_dragIndex];
            pt.Temperature = Math.Round(pt.Temperature);
            pt.Percent = Math.Round(pt.Percent);
        }

        _dragIndex = -1;
        e.Pointer.Capture(null);
        Commit();
        InvalidateVisual();
    }

    private int FindPointNear(Rect plot, Point pos)
    {
        if (Points is null) return -1;
        for (int i = 0; i < Points.Count; i++)
        {
            var c = new Point(X(plot, Points[i].Temperature), Y(plot, Points[i].Percent));
            if (Math.Abs(c.X - pos.X) <= HitRadius && Math.Abs(c.Y - pos.Y) <= HitRadius)
                return i;
        }
        return -1;
    }

    private void Commit()
    {
        if (ChangedCommand?.CanExecute(null) == true)
            ChangedCommand.Execute(null);
    }

    // ---- Rendering ----

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);
        var plot = Plot();
        bool dark = ActualThemeVariant == ThemeVariant.Dark;

        var gridBrush = new SolidColorBrush(dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0));
        var gridPen = new Pen(gridBrush, 1);
        var textBrush = new SolidColorBrush(dark ? Color.FromArgb(150, 235, 235, 245) : Color.FromArgb(150, 60, 60, 67));
        var accent = dark ? Color.FromRgb(10, 132, 255) : Color.FromRgb(0, 122, 255);
        var lineBrush = new SolidColorBrush(accent);
        var linePen = new Pen(lineBrush, 2.2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var fillBrush = new SolidColorBrush(Color.FromArgb(dark ? (byte)38 : (byte)28, accent.R, accent.G, accent.B));
        var pointFill = new SolidColorBrush(dark ? Color.FromRgb(28, 28, 30) : Colors.White);
        var markerColor = dark ? Color.FromRgb(48, 209, 88) : Color.FromRgb(52, 199, 89);

        // Panel background
        ctx.DrawRectangle(
            new SolidColorBrush(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255)),
            null, new RoundedRect(new Rect(0, 0, Bounds.Width, Bounds.Height), 10));

        // Grid + labels
        for (int pct = 0; pct <= 100; pct += 25)
        {
            double y = Y(plot, pct);
            ctx.DrawLine(gridPen, new Point(plot.X, y), new Point(plot.Right, y));
            DrawText(ctx, $"{pct}%", new Point(4, y - 7), textBrush, 9);
        }
        for (int temp = 20; temp <= 110; temp += 15)
        {
            double x = X(plot, temp);
            ctx.DrawLine(gridPen, new Point(x, plot.Y), new Point(x, plot.Bottom));
            DrawText(ctx, $"{temp}°", new Point(x - 8, plot.Bottom + 4), textBrush, 9);
        }

        var pts = Points?.OrderBy(p => p.Temperature).ToList();
        if (pts is { Count: > 0 })
        {
            // Fill under the curve
            var fill = new StreamGeometry();
            using (var g = fill.Open())
            {
                g.BeginFigure(new Point(X(plot, pts[0].Temperature), plot.Bottom), true);
                g.LineTo(new Point(X(plot, pts[0].Temperature), Y(plot, pts[0].Percent)));
                foreach (var p in pts)
                    g.LineTo(new Point(X(plot, p.Temperature), Y(plot, p.Percent)));
                g.LineTo(new Point(X(plot, pts[^1].Temperature), plot.Bottom));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(fillBrush, null, fill);

            // Flat leading/trailing segments to the plot edges + the curve line
            double yFirst = Y(plot, pts[0].Percent), yLast = Y(plot, pts[^1].Percent);
            ctx.DrawLine(linePen, new Point(plot.X, yFirst), new Point(X(plot, pts[0].Temperature), yFirst));
            for (int i = 0; i < pts.Count - 1; i++)
                ctx.DrawLine(linePen,
                    new Point(X(plot, pts[i].Temperature), Y(plot, pts[i].Percent)),
                    new Point(X(plot, pts[i + 1].Temperature), Y(plot, pts[i + 1].Percent)));
            ctx.DrawLine(linePen, new Point(X(plot, pts[^1].Temperature), yLast), new Point(plot.Right, yLast));

            // Point handles
            foreach (var p in pts)
            {
                var c = new Point(X(plot, p.Temperature), Y(plot, p.Percent));
                ctx.DrawEllipse(pointFill, linePen, c, 5, 5);
            }
        }

        // Live operating marker
        if (CurrentTemperature is { } ct && ct >= TempMin && ct <= TempMax)
        {
            double x = X(plot, Math.Clamp(ct, TempMin, TempMax));
            var dashPen = new Pen(new SolidColorBrush(markerColor) { Opacity = 0.7 }, 1, new DashStyle(new double[] { 3, 3 }, 0));
            ctx.DrawLine(dashPen, new Point(x, plot.Y), new Point(x, plot.Bottom));

            if (CurrentPercent is { } cp)
            {
                var c = new Point(x, Y(plot, Math.Clamp(cp, PctMin, PctMax)));
                ctx.DrawEllipse(new SolidColorBrush(markerColor), null, c, 4.5, 4.5);
                DrawText(ctx, $"{ct:0}°  {cp:0}%", new Point(Math.Min(x + 8, plot.Right - 54), plot.Y + 2),
                    new SolidColorBrush(markerColor), 10);
            }
        }
    }

    private static void DrawText(DrawingContext ctx, string text, Point at, IBrush brush, double size)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, size, brush);
        ctx.DrawText(ft, at);
    }
}
