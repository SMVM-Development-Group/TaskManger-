using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskManagerPlusPlus;

internal enum ProcessHistoryMetric
{
    Cpu,
    Ram,
    DedicatedGpu,
    SharedGpu
}

public partial class ProcessHistoryWindow : Window
{
    private readonly IReadOnlyList<ProcessUsageSample> _samples;
    private readonly ProcessHistoryMetric _metric;
    private readonly string _processName;
    private readonly int _processId;

    internal ProcessHistoryWindow(
        string processName,
        int processId,
        IReadOnlyList<ProcessUsageSample> samples,
        ProcessHistoryMetric metric)
    {
        InitializeComponent();
        _processName = processName;
        _processId = processId;
        _samples = samples;
        _metric = metric;
        ChartCanvas.SizeChanged += (_, _) => DrawChart();
        Loaded += (_, _) => DrawChart();
    }

    private void DrawChart()
    {
        ChartCanvas.Children.Clear();
        var title = _metric switch
        {
            ProcessHistoryMetric.Cpu => "CPU",
            ProcessHistoryMetric.Ram => "RAM in use",
            ProcessHistoryMetric.DedicatedGpu => "Dedicated VRAM",
            ProcessHistoryMetric.SharedGpu => "Shared GPU memory",
            _ => throw new ArgumentOutOfRangeException()
        };
        Title = $"{title} history — {_processName} (PID {_processId})";
        TitleText.Text = Title;

        var data = _samples.Select(sample => new ChartValue(
                sample.Timestamp,
                _metric switch
                {
                    ProcessHistoryMetric.Cpu => sample.CpuPercent,
                    ProcessHistoryMetric.Ram => sample.WorkingSetBytes,
                    ProcessHistoryMetric.DedicatedGpu => sample.DedicatedGpuBytes,
                    ProcessHistoryMetric.SharedGpu => sample.SharedGpuBytes,
                    _ => null
                }))
            .Where(item => item.Value.HasValue)
            .ToArray();

        if (data.Length == 0)
        {
            SummaryText.Text = _metric is ProcessHistoryMetric.DedicatedGpu or ProcessHistoryMetric.SharedGpu
                ? "No GPU memory samples are available for this process. This can mean the process is not using the GPU or Windows GPU counters are unavailable."
                : "No history samples have been collected yet.";
            return;
        }

        var values = data.Select(item => item.Value!.Value).ToArray();
        var maximum = values.Max();
        var minimum = values.Min();
        var average = values.Average();
        var unit = _metric == ProcessHistoryMetric.Cpu ? "%" : "bytes";
        SummaryText.Text = $"Now {FormatValue(values[^1])} · 30-minute average {FormatValue(average)} · peak {FormatValue(maximum)} · "
            + $"{data[0].Timestamp:HH:mm:ss}–{data[^1].Timestamp:HH:mm:ss}";

        var width = ChartCanvas.ActualWidth;
        var height = ChartCanvas.ActualHeight;
        if (width <= 80 || height <= 80)
            return;

        const double left = 64;
        const double right = 18;
        const double top = 18;
        const double bottom = 38;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var rangeMaximum = Math.Max(maximum * 1.1, _metric == ProcessHistoryMetric.Cpu ? 10 : 1);
        var rangeMinimum = Math.Min(0, minimum);

        for (var index = 0; index <= 4; index++)
        {
            var y = top + plotHeight * index / 4;
            var line = new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(0x27, 0x34, 0x4A)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(line);
            var axisValue = rangeMaximum - (rangeMaximum - rangeMinimum) * index / 4;
            AddLabel(FormatValue(axisValue), 2, y - 9, 58, 18, rightAligned: true);
        }

        var points = new PointCollection();
        for (var index = 0; index < data.Length; index++)
        {
            var x = left + (data.Length == 1 ? plotWidth / 2 : plotWidth * index / (data.Length - 1));
            var ratio = (data[index].Value!.Value - rangeMinimum) / Math.Max(rangeMaximum - rangeMinimum, 1);
            var y = top + plotHeight * (1 - ratio);
            points.Add(new Point(x, y));
        }

        var graph = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0xD9, 0xC2)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        ChartCanvas.Children.Add(graph);
        AddLabel(data[0].Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture), left, height - 28, 90, 20);
        AddLabel(data[^1].Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture),
            Math.Max(left + plotWidth - 90, left), height - 28, 90, 20, rightAligned: true);
        AddLabel(unit, 4, 2, 60, 16, rightAligned: true);
    }

    private void AddLabel(string text, double left, double top, double width, double height, bool rightAligned = false)
    {
        var label = new System.Windows.Controls.TextBlock
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

    private string FormatValue(double value)
    {
        if (_metric == ProcessHistoryMetric.Cpu)
            return $"{value:N1}%";
        if (value <= 0)
            return "0 B";
        return MainWindow.FormatBytes((ulong)value);
    }

    private sealed record ChartValue(DateTime Timestamp, double? Value);
}
