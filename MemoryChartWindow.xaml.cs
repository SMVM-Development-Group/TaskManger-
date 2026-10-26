using System.Globalization;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskManagerPlusPlus;

public partial class MemoryChartWindow : Window
{
    private static readonly Color[] ProcessColors =
    [
        Color.FromRgb(0x66, 0xD9, 0xC2),
        Color.FromRgb(0x78, 0xA9, 0xF5),
        Color.FromRgb(0xF5, 0xB9, 0x5C),
        Color.FromRgb(0xD9, 0x83, 0xE5),
        Color.FromRgb(0xF0, 0x78, 0x78),
        Color.FromRgb(0x79, 0xD0, 0x83),
        Color.FromRgb(0xE6, 0xD5, 0x6D),
        Color.FromRgb(0x5F, 0xCF, 0xD8),
        Color.FromRgb(0xE8, 0x93, 0x65),
        Color.FromRgb(0xA7, 0x96, 0xF2),
        Color.FromRgb(0x68, 0x73, 0x89)
    ];
    private const double ChartSize = 280;
    private const double ChartRadius = 128;

    public MemoryChartWindow(ulong usedBytes, IReadOnlyList<(string Name, int Id, ulong Bytes)> processMemory)
    {
        InitializeComponent();

        var processWorkingSetTotal = processMemory.Aggregate(0UL, (total, process) => total + process.Bytes);
        SystemMemoryText.Text = $"{FormatBytes(usedBytes)} physical RAM in use system-wide";
        ProcessMemoryTotalText.Text = $"{FormatBytes(processWorkingSetTotal)} combined process working sets";

        var chartSlices = new List<ChartSlice>();
        var otherProcessBytes = processWorkingSetTotal;
        foreach (var process in processMemory
                     .OrderByDescending(process => process.Bytes)
                     .Take(10))
        {
            chartSlices.Add(new ChartSlice(
                $"{process.Name} (PID {process.Id})",
                process.Bytes,
                ProcessColors[chartSlices.Count]));
            otherProcessBytes -= process.Bytes;
        }

        if (otherProcessBytes > 0)
            chartSlices.Add(new ChartSlice("Other processes", otherProcessBytes, ProcessColors[10]));

        var legendItems = new ObservableCollection<LegendItem>();
        var accumulatedAngle = -90d;
        foreach (var slice in chartSlices)
        {
            var proportion = processWorkingSetTotal == 0 ? 0 : (double)slice.Bytes / processWorkingSetTotal;
            legendItems.Add(new LegendItem(
                slice.Label,
                $"{FormatBytes(slice.Bytes)}  ·  {proportion * 100:N1}% of process working sets",
                new SolidColorBrush(slice.Color)));
            AddSlice(proportion, slice.Color, accumulatedAngle);
            accumulatedAngle += proportion * 360d;
        }
        LegendList.ItemsSource = legendItems;
    }

    private void AddSlice(double ratio, Color color, double startAngle)
    {
        if (ratio <= 0)
            return;

        var fill = new SolidColorBrush(color);
        if (ratio >= 1)
        {
            ChartCanvas.Children.Add(new Ellipse
            {
                Width = ChartRadius * 2,
                Height = ChartRadius * 2,
                Fill = fill,
                Stroke = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x20)),
                StrokeThickness = 2
            });
            return;
        }

        var center = ChartSize / 2;
        var endAngle = startAngle + ratio * 360d;
        var start = PointOnCircle(center, center, ChartRadius, startAngle);
        var end = PointOnCircle(center, center, ChartRadius, endAngle);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(center, center), isFilled: true, isClosed: true);
            context.LineTo(start, isStroked: true, isSmoothJoin: true);
            context.ArcTo(
                end,
                new Size(ChartRadius, ChartRadius),
                rotationAngle: 0,
                isLargeArc: ratio > 0.5,
                sweepDirection: SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: true);
        }
        geometry.Freeze();

        ChartCanvas.Children.Add(new Path
        {
            Data = geometry,
            Fill = fill,
            Stroke = new SolidColorBrush(Color.FromRgb(0x0B, 0x10, 0x20)),
            StrokeThickness = 2
        });
    }

    private static Point PointOnCircle(double centerX, double centerY, double radius, double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180;
        return new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value.ToString("N1", CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private sealed record ChartSlice(string Label, ulong Bytes, Color Color);

    private sealed record LegendItem(string Label, string ValueText, Brush Color);
}
