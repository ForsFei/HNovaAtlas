using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace IndustrialOpenCvSharpWpf;

public sealed class MainWindow : Window
{
    private readonly FrameSession session = new(NativeFrameRunner.Analyze);
    private readonly MeasurementViewer viewer = new();
    private readonly Button next = new() { Content = "下一张离线图", Margin = new Thickness(4) };
    private readonly Button measure = new() { Content = "测量像素宽度", Margin = new Thickness(4) };
    private readonly Button clear = new() { Content = "清空结果", Margin = new Thickness(4) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private int index;
    private bool draining, allowClose;
    public MainWindow()
    {
        Title = "HNova · OpenCvSharp离线宽度查看器（非产品判定）"; Width = 760; Height = 540; MinWidth = 500; MinHeight = 380;
        DockPanel root = new() { Margin = new Thickness(12) };
        StackPanel actions = new() { Orientation = Orientation.Horizontal };
        actions.Children.Add(next); actions.Children.Add(measure); actions.Children.Add(clear);
        DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        TextBlock legend = new() { Text = "青色：预设ROI；橙色：唯一测量的代表跨度。无毫米/OK判定。", Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(legend, Dock.Bottom); root.Children.Add(legend); root.Children.Add(viewer); Content = root;
        next.Click += (_, _) => { index = (index + 1) % 4; session.Load(FrameFactory.Create(index)); Render(); };
        clear.Click += (_, _) => { session.Clear(); Render(); };
        measure.Click += async (_, _) => { if (!session.TryRun(out Task pending)) return; Render(); await pending; if (!draining) Render(); };
        Closing += OnClosing;
        session.Load(FrameFactory.Create(0)); Render();
    }
    private void Render()
    {
        Dispatcher.VerifyAccess(); FrameView view = session.View; viewer.Present(view);
        measure.IsEnabled = !view.Closing && !view.Busy && view.Selected is not null;
        next.IsEnabled = clear.IsEnabled = !view.Closing;
        status.Text = $"本地版本{view.Revision} · {view.Selected?.Name} · " + (view.Closing ? "等待在途调用退出；不强制中断原生算法" :
            view.Busy ? "后台处理；换图/清空会丢弃迟到结果，无任务排队" :
            view.Error is { } error ? $"处理失败：{error}；无回退，请核对本项目运行依赖。" :
            view.Report is { } report ? $"{report.Outcome}，边{report.Edges.Count}/对{report.Pairs.Count}，宽度{report.WidthPixels?.ToString("F3") ?? "未发布"} px；非完整性/质量验收" : "未测量；仅显示离线图及预设ROI。");
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (draining) return;
        draining = true; Task pending = session.CloseAsync(); Render();
        await pending;
        await Dispatcher.InvokeAsync(() => { allowClose = true; Close(); });
    }
}
