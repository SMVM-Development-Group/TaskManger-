using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TaskManagerPlusPlus;

internal enum SystemHistoryMetric
{
    Cpu,
    UsedRam,
    AvailableRam,
    RamPercent
}

public partial class SystemHistoryWindow : Window
{
    private static readonly TimeSpan[] DisplayRanges =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30)
    ];
    private readonly Func<IReadOnlyList<SystemUsageSample>> _getSamples;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    internal SystemHistoryWindow(Func<IReadOnlyList<SystemUsageSample>> getSamples)
    {
        InitializeComponent();
        _getSamples = getSamples;
        MetricBox.SelectionChanged += MetricBox_SelectionChanged;
        RangeBox.SelectionChanged += RangeBox_SelectionChanged;
        ChartCanvas.SizeChanged += (_, _) => DrawChart();
        _refreshTimer.Tick += (_, _) => DrawChart();
        Closed += (_, _) => _refreshTimer.Stop();
        Loaded += (_, _) => _refreshTimer.Start();
        Loaded += (_, _) => DrawChart();
    }

    private void MetricBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => DrawChart();

    private void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        if (MetricBox.SelectedIndex < 0 || RangeBox.SelectedIndex < 0)
            return;

        var metric = (SystemHistoryMetric)MetricBox.SelectedIndex;
        var displayRange = DisplayRanges[RangeBox.SelectedIndex];
        var rangeEndUtc = DateTime.UtcNow;
        var rangeStartUtc = rangeEndUtc - displayRange;
        var values = _getSamples()
            .Where(sample => sample.Timestamp >= rangeStartUtc && sample.Timestamp <= rangeEndUtc)
            .Select(sample => new ChartValue(sample.Timestamp, GetValue(sample, metric)))
            .ToArray();
        if (values.Length == 0)
        {
            SummaryText.Text = $"No samples in the last {FormatDuration(displayRange)}. Leave TaskManager++ open and the graph will fill as samples are collected.";
            return;
        }

        var samples = values.Select(item => item.Value).ToArray();
        var maximum = samples.Max();
        var average = samples.Average();
        var unit = metric switch
        {
            SystemHistoryMetric.Cpu or SystemHistoryMetric.RamPercent => "%",
            _ => "bytes"
        };
        SummaryText.Text = $"{values.Length} samples · latest {FormatValue(samples[^1], metric)} · {FormatDuration(displayRange)} average {FormatValue(average, metric)} · "
            + $"peak {FormatValue(maximum, metric)} · {values[0].Timestamp.ToLocalTime():HH:mm:ss}–{values[^1].Timestamp.ToLocalTime():HH:mm:ss}";

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 80 || height <= 80)
            return;

        const double left = 76;
        const double right = 18;
        const double top = 18;
        const double bottom = 38;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var rangeMaximum = metric switch
        {
            SystemHistoryMetric.Cpu or SystemHistoryMetric.RamPercent => 100,
            _ => Math.Max(maximum * 1.1, 1)
        };

        for (var index = 0; index <= 4; index++)
        {
            var y = top + plotHeight * index / 4;
            ChartCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(0x35, 0x43, 0x5A)),
                StrokeThickness = 1
            });
            AddLabel(FormatValue(rangeMaximum * (4 - index) / 4, metric), 2, y - 9, 68, 18, true);
        }

        for (var index = 0; index <= 2; index++)
        {
            var x = left + plotWidth * index / 2;
            ChartCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = top + plotHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(0x27, 0x34, 0x4A)),
                StrokeThickness = 1
            });
            var tickTime = rangeStartUtc + TimeSpan.FromTicks(displayRange.Ticks * index / 2);
            var label = tickTime.ToLocalTime().ToString(
                displayRange <= TimeSpan.FromMinutes(5) ? "HH:mm:ss" : "HH:mm",
                CultureInfo.CurrentCulture);
            AddLabel(label, x - 45, height - 28, 90, 20, rightAligned: index == 2);
        }

        var points = new PointCollection(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            var elapsed = values[index].Timestamp - rangeStartUtc;
            var x = left + plotWidth * elapsed.TotalMilliseconds / displayRange.TotalMilliseconds;
            var y = top + plotHeight * (1 - values[index].Value / rangeMaximum);
            points.Add(new Point(x, y));
        }

        ChartCanvas.Children.Add(new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0xD9, 0xC2)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        });
        var pointBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xD9, 0xC2));
        foreach (var point in points)
        {
            ChartCanvas.Children.Add(new Ellipse
            {
                Width = points.Count == 1 ? 8 : 4,
                Height = points.Count == 1 ? 8 : 4,
                Fill = pointBrush
            });
            var markerSize = points.Count == 1 ? 8 : 4;
            Canvas.SetLeft(ChartCanvas.Children[^1], point.X - markerSize / 2);
            Canvas.SetTop(ChartCanvas.Children[^1], point.Y - markerSize / 2);
        }
        AddLabel(unit, 4, 2, 68, 16, true);
    }

    private static double GetValue(SystemUsageSample sample, SystemHistoryMetric metric) =>
        metric switch
        {
            SystemHistoryMetric.Cpu => sample.CpuPercent,
            SystemHistoryMetric.UsedRam => sample.UsedRamBytes,
            SystemHistoryMetric.AvailableRam => sample.AvailableRamBytes,
            SystemHistoryMetric.RamPercent => sample.TotalRamBytes == 0
                ? 0
                : sample.UsedRamBytes * 100d / sample.TotalRamBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(metric))
        };

    private static string FormatValue(double value, SystemHistoryMetric metric)
    {
        if (metric is SystemHistoryMetric.Cpu or SystemHistoryMetric.RamPercent)
            return $"{value:N1}%";
        return value <= 0 ? "0 B" : MainWindow.FormatBytes((ulong)value);
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalMinutes >= 1
            ? $"{duration.TotalMinutes:N0}-minute"
            : $"{duration.TotalSeconds:N0}-second";

    private void AddLabel(string text, double left, double top, double width, double height, bool rightAligned = false)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = width,
            Height = height,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x89, 0x96, 0xAC)),
            TextAlignment = rightAligned ? TextAlignment.Right : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Canvas.SetLeft(label, left);
        Canvas.SetTop(label, top);
        ChartCanvas.Children.Add(label);
    }

    private sealed record ChartValue(DateTime Timestamp, double Value);
}
