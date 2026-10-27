using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IndustrialVision;

namespace IndustrialHalconWpf;

public sealed class MainWindow : Window
{
    private readonly FrameSession session = new(NativeFrameRunner.Analyze);
    private readonly Mono8Image original = PlainPgm.LoadFixture();
    private readonly Image picture = new() { Width = 400, Height = 300, Stretch = Stretch.Fill };
    private readonly Canvas overlay = new() { Width = 400, Height = 300, IsHitTestVisible = false };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly Button next = new() { Content = "切换原图/反相", Margin = new Thickness(4) };
    private readonly Button run = new() { Content = "执行 HALCON", Margin = new Thickness(4) };
    private readonly Button clear = new() { Content = "清空结果", Margin = new Thickness(4) };
    private bool inverted, draining, allowClose;
    public MainWindow()
    {
        Title = "HNova · HALCON 离线单次调用（需要原生环境）"; Width = 650; Height = 510;
        StackPanel root = new() { Margin = new Thickness(12) };
        StackPanel actions = new() { Orientation = Orientation.Horizontal };
        actions.Children.Add(next); actions.Children.Add(run); actions.Children.Add(clear);
        Grid display = new() { Width = 400, Height = 300, HorizontalAlignment = HorizontalAlignment.Left };
        display.Children.Add(picture); display.Children.Add(overlay);
        root.Children.Add(actions); root.Children.Add(display); root.Children.Add(status); Content = root;
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        next.Click += (_, _) => { inverted = !inverted; session.Load(inverted ? Mono8Image.FromPacked(8, 6, original.CopyPixels().Select(v => (byte)(255 - v)).ToArray()) : original); Render(); };
        clear.Click += (_, _) => { session.Clear(); Render(); };
        run.Click += async (_, _) => { if (!session.TryRun(out Task pending)) return; Render(); await pending; if (!draining) Render(); };
        Closing += OnClosing;
        session.Load(original); Render();
    }
    private void Render()
    {
        Dispatcher.VerifyAccess();
        FrameView view = session.View; overlay.Children.Clear();
        if (view.Image is { } frame)
        {
            BitmapSource bitmap = BitmapSource.Create(8, 6, 96, 96, PixelFormats.Gray8, null, frame.CopyPixels(), 8);
            bitmap.Freeze(); picture.Source = bitmap;
        }
        if (view.Report is { } report)
            foreach (var feature in report.Candidates)
            {
                Ellipse marker = new() { Width = 12, Height = 12, Stroke = Brushes.Red, StrokeThickness = 2 };
                Canvas.SetLeft(marker, (feature.ColumnPixels + .5) * 50 - 6);
                Canvas.SetTop(marker, (feature.RowPixels + .5) * 50 - 6); overlay.Children.Add(marker);
            }
        run.IsEnabled = !view.Busy && !view.Closing;
        next.IsEnabled = clear.IsEnabled = !view.Closing;
        status.Text = $"显示版本 {view.Revision} · " + (view.Closing ? "等待在途调用退出，未强制中断原生算法" : view.Busy ? "后台执行中；可切图，旧结果不会覆盖新图" :
            view.Error is { } error ? $"调用失败：{error}；无回退、无产品判定。请核对24.11.3原生环境/过程。" :
            view.Report is { } result ? $"{result.Outcome}，候选 {result.Candidates.Count}；红点仅表示区域中心，不是轮廓或产品OK" : "未运行；只显示离线图像。执行需要合法HALCON运行环境。");
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (draining) return;
        draining = true;
        Task pending = session.CloseAsync(); Render();
        await pending;
        // Closing must finish before calling Close again, even when pending was already complete.
        await Dispatcher.InvokeAsync(() => { allowClose = true; Close(); });
    }
}
