using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IndustrialYolo.Images;

namespace IndustrialYolo.Wpf;

public sealed class MainWindow : Window
{
    private readonly string model;
    private readonly DisplaySession session = new();
    private readonly YoloViewer viewer = new();
    private readonly TextBlock status = new() { Text = "打开可信本地PNG/JPEG，然后检测。固定COCO模型；类别索引不是工业缺陷名称。", TextWrapping = TextWrapping.Wrap };
    private readonly Button load = new() { Content = "打开图片", Padding = new(12, 6, 12, 6), Margin = new(0, 0, 10, 0) };
    private readonly Button infer = new() { Content = "检测当前图", Padding = new(12, 6, 12, 6), Margin = new(0, 0, 10, 0) };
    private readonly Button clear = new() { Content = "清空", Padding = new(12, 6, 12, 6) };
    private Task? loading, processing, initializing;
    private readonly YoloInferenceWorker? worker;
    private long loadVersion, workNumber;
    private bool closing, closeAllowed, workerReady;

    public MainWindow(string modelPath, string? initialImage, bool reuseSession = false)
    {
        model = System.IO.Path.GetFullPath(modelPath);
        if (reuseSession) worker = new(model, capacity: 2);
        Title = "HNova Atlas · YOLOX离线图片"; Width = 1000; Height = 760; MinWidth = 560; MinHeight = 420;
        DockPanel panel = new() { Margin = new(16) };
        StackPanel header = new();
        header.Children.Add(new TextBlock { Text = "原图与同图检测框", FontSize = 22, Margin = new(0, 0, 0, 12) });
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 12) };
        buttons.Children.Add(load); buttons.Children.Add(infer); buttons.Children.Add(clear); header.Children.Add(buttons);
        status.Margin = new(0, 0, 0, 12); header.Children.Add(status); DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header); panel.Children.Add(viewer); Content = panel;
        load.Click += async (_, _) =>
        {
            OpenFileDialog dialog = new() { Filter = "PNG/JPEG|*.png;*.jpg;*.jpeg", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) { loading = LoadAsync(dialog.FileName); await loading; }
        };
        infer.Click += async (_, _) => { processing = InferAsync(); await processing; };
        clear.Click += (_, _) => { loadVersion++; session.Clear(); status.Text = "显示已清空；后台任务返回时不会重新叠加。"; Present(); };
        Closing += OnClosing;
        Loaded += async (_, _) =>
        {
            if (worker is not null) { initializing = ObserveReadyAsync(); await initializing; }
            if (!closing && initialImage is not null) { loading = LoadAsync(initialImage); await loading; }
        };
        Present();
    }
    private async Task ObserveReadyAsync()
    {
        try { await worker!.Ready; workerReady = true; if (!closing) status.Text = "CPU会话已初始化；打开图片后检测。"; }
        catch (Exception error)
        {
            if (!closing) status.Text = $"模型初始化失败：{error.GetType().Name}；关闭后重新建立会话。";
            try { await worker!.Completion; } catch (Exception failure) { Console.Error.WriteLine(failure.Message); }
        }
        finally { Present(); }
    }
    private async Task LoadAsync(string path)
    {
        long version = ++loadVersion; session.Clear(); status.Text = "读取图片……"; load.IsEnabled = false; Present();
        try
        {
            ImageSnapshot image = await Task.Run(() => ImageSnapshot.Create(OpenCvImageInput.Load(path)));
            if (!closing && version == loadVersion)
            {
                session.Show(image);
                status.Text = $"{System.IO.Path.GetFileName(path)} · {image.Source.Bgr.Width}×{image.Source.Bgr.Height}，尚未检测。";
            }
        }
        catch (Exception error) { if (!closing && version == loadVersion) status.Text = $"读取失败：{error.GetType().Name}：{error.Message}"; }
        finally { load.IsEnabled = !closing; Present(); }
    }
    private async Task InferAsync()
    {
        InferenceRequest? request = session.TryBegin(); if (request is null) return;
        status.Text = "CPU检测中；可以切换图片，旧结果不会叠加到新图。"; Present();
        YoloTerminal? terminal = null;
        try
        {
            OverlayResult result;
            if (worker is null) result = await Task.Run(() => OfflineDetector.Run(model, request.Image));
            else
            {
                YoloSubmission submission = worker.TrySubmit(new(worker.Producer, checked(++workNumber), request.Image));
                if (submission.Admission != YoloAdmission.Accepted) throw new InvalidOperationException($"Worker admission: {submission.Admission}");
                terminal = await submission.Completion!;
                if (terminal.Stage != YoloStage.Completed) throw new InvalidOperationException(terminal.Error);
                result = terminal.Output!;
            }
            Attachment attached = session.Complete(request, result);
            if (!closing && attached == Attachment.Attached)
                status.Text = $"本图NMS保留{result.Kept}项，可见{result.Boxes.Count}项。score>0.1；空结果正常，未作产品OK/NG判定。";
        }
        catch (Exception error)
        {
            Attachment attached = session.Complete(request, null, error.Message);
            if (!closing && attached == Attachment.Failed) status.Text = $"检测失败：{error.GetType().Name}：{error.Message}";
        }
        finally
        {
            // Consumed locally even if stale for the display. This is NOT PLC/product acknowledgement.
            if (terminal is not null && !worker!.Acknowledge(terminal))
            { worker.Complete(); session.Clear(); if (!closing) status.Text = "结果收据不匹配；停止新任务。"; }
            Present();
        }
    }
    private void Present()
    {
        Dispatcher.VerifyAccess(); viewer.Present(session.Current, session.Result);
        infer.IsEnabled = !closing && !session.Busy && session.Current is not null &&
            (worker is null || (workerReady && !worker.Snapshot.Closed));
        clear.IsEnabled = !closing;
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true; if (closing) return;
        closing = true; load.IsEnabled = false; loadVersion++; session.BeginClose(); Present(); status.Text = "等待当前文件读取/推理退出……";
        worker?.Complete();
        if (initializing is not null) await initializing;
        if (loading is not null) await loading;
        if (processing is not null) await processing;
        if (worker is not null)
        {
            try { await worker.Ready; } catch (Exception error) { Console.Error.WriteLine(error.Message); }
            try { await worker.Completion; } catch (Exception error) { Console.Error.WriteLine(error.Message); }
        }
        closeAllowed = true; Close();
    }
}
