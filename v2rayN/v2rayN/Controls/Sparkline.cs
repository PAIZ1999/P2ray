using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace v2rayN.Controls;

/// <summary>
/// 轻量速率折线图。
/// 不依赖第三方图表库：直接生成 StreamGeometry 画折线 + 面积填充。
/// 数据为 0..1 的比例序列，会按控件尺寸等比缩放。
/// </summary>
public class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(
            nameof(Values),
            typeof(IEnumerable),
            typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(Sparkline),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(
            nameof(Fill),
            typeof(Brush),
            typeof(Sparkline),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(Sparkline),
            new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>数据序列，元素需可转为 double。</summary>
    public IEnumerable? Values
    {
        get => (IEnumerable?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var points = ToPoints();
        if (points.Count < 2)
        {
            // 无数据时不绘制：虚线易被误读为"尚未完成"
            return;
        }

        var max = points.Max();
        var scale = max <= 0 ? 0 : (h - 4) / max;

        // 折线
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            var first = true;
            for (var i = 0; i < points.Count; i++)
            {
                var x = w * i / (points.Count - 1);
                var y = h - 2 - points[i] * scale;
                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        line.Freeze();

        // 面积填充
        if (Fill is not null)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                for (var i = 0; i < points.Count; i++)
                {
                    var x = w * i / (points.Count - 1);
                    var y = h - 2 - points[i] * scale;
                    ctx.LineTo(new Point(x, y), true, false);
                }

                ctx.LineTo(new Point(w, h), true, false);
            }

            area.Freeze();
            dc.DrawGeometry(Fill, null, area);
        }

        var pen = new Pen(Stroke, StrokeThickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        pen.Freeze();

        dc.DrawGeometry(null, pen, line);
    }

    private List<double> ToPoints()
    {
        var list = new List<double>();
        if (Values is null)
        {
            return list;
        }

        foreach (var item in Values)
        {
            if (item is null)
            {
                continue;
            }

            try
            {
                list.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture));
            }
            catch
            {
                list.Add(0);
            }
        }

        return list;
    }
}
