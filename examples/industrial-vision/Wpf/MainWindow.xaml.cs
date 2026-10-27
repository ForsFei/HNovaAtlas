using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace IndustrialVision.Wpf;

public partial class MainWindow : Window
{
    private readonly Guid owner = Guid.NewGuid(), syntheticCamera = Guid.NewGuid();
    private readonly OfflineFrameSource source;
    private readonly InspectionPipeline pipeline;
    private readonly FrameDisplayState display;
    private Task? processing;
    private uint requestNumber;
    private ulong syntheticTrigger;
    private bool busy, closing, closeAllowed, faulted;
    private readonly bool shutdownOnClose;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task WhenProcessingIdle => processing ?? Task.CompletedTask;
    internal Task WhenClosed => closed.Task;
    public MainWindow() : this(shutdownOnClose: true) { }
    internal MainWindow(bool shutdownOnClose)
    {
        this.shutdownOnClose = shutdownOnClose;
        InitializeComponent();
        Mono8Image first = PlainPgm.LoadFixture();
        byte[] inverted = first.CopyPixels();
        for (int i = 0; i < inverted.Length; i++) inverted[i] = (byte)(255 - inverted[i]);
        source = new([first, Mono8Image.FromPacked(first.Width, first.Height, inverted)]); source.Open();
        ProcessingRecipe recipe = new("roi-statistics-v1", 1, new(1, 1, 2, 2), 200);
        pipeline = new(owner, first.Width, first.Height, recipe, capacity: 2, measure: async (image, plan) =>
        {
            await Task.Delay(400).ConfigureAwait(false); // Deliberate visual teaching delay, NOT measured latency.
            return image.Measure(plan.Region, plan.Threshold);
        });
        display = new(owner, pipeline.Session, recipe);
        ShowNext();
    }
    private void OnNext(object sender, RoutedEventArgs e) => ShowNext();
    private void ShowNext()
    {
        if (closing) return;
        try
        {
            if (requestNumber == uint.MaxValue || syntheticTrigger == ulong.MaxValue) throw new InvalidOperationException("离线编号已用尽，请退出重新建立示例。");
            using FrameLease lease = source.Acquire();
            OwnedFrame owned = lease.CopyToOwned();
            // Synthetic application declarations only. No real trigger association is established here.
            CaptureResult capture = new(new(new(owner, ++requestNumber), $"offline-{requestNumber}", 1),
                new(owned, syntheticCamera, 1, ++syntheticTrigger, 0, Complete: true, IdentityQualified: true));
            if (!display.Show(capture)) throw new InvalidOperationException("离线显示输入未通过校验。");
            StatusText.Text = "已切换预览。处理期间也可切换；旧结果不会叠加到新图。";
            PixelText.Text = "点击图像查看原图像素；黑色留白不可选。点击不会生成毫米测量或运动。";
            Present();
        }
        catch (Exception error) { faulted = true; StatusText.Text = $"离线源/显示失败：{error.GetType().Name}；停止新处理。"; UpdateButtons(); }
    }
    private async void OnProcess(object sender, RoutedEventArgs e)
    {
        if (busy || closing || faulted || display.Current is null) return;
        processing = ProcessCurrentAsync(display.Current);
        await processing;
    }
    private async Task ProcessCurrentAsync(CaptureResult capture)
    {
        busy = true; UpdateButtons();
        try
        {
            InspectionSubmission submitted = pipeline.TrySubmit(capture);
            if (submitted.Admission != InspectionAdmission.Accepted)
            {
                StatusText.Text = $"没有新接收处理任务：{submitted.Admission}。没有重拍或隐藏重试。"; return;
            }
            StatusText.Text = $"正在处理 {capture.Request.Workpiece}，可切换下一帧观察迟到结果隔离。";
            InspectionResult result = await submitted.Result!; // WPF event context resumes on UI thread.
            Dispatcher.VerifyAccess();
            if (!closing)
            {
                bool attached = display.TryAttach(result);
                StatusText.Text = attached ? "当前帧处理已返回；统计完成不等于产品合格。"
                    : $"{result.Request.Workpiece} 的结果已返回，但当前已切帧/清空，未叠加。";
                Present();
            }
            // Local demonstration consumes/report-disposes even nondisplayed results. No durable/PLC acknowledgement.
            if (!pipeline.Acknowledge(result)) throw new InvalidOperationException("结果收据不匹配。");
        }
        catch (Exception error) { faulted = true; StatusText.Text = $"处理/显示交接失败：{error.GetType().Name}；停止新处理。"; }
        finally { busy = false; UpdateButtons(); }
    }
    private void OnClear(object sender, RoutedEventArgs e)
    {
        display.Clear(); PixelText.Text = "显示已清空。"; StatusText.Text = "只清空显示，不取消已接收处理，也不停止相机或运动。"; Present();
    }
    private void Present()
    {
        Viewer.Present(display);
        FrameText.Text = display.Current is { } current ? $"当前：{current.Request.Workpiece} · 帧 {current.Evidence.Frame.Stamp.Number} · 8×6 Mono8"
            : "当前：无图像";
        ResultText.Text = display.Result is { Stage: ProcessingStage.Completed, Measurement: { } statistics }
            ? $"同帧ROI (1,1,2,2)：均值 {statistics.Mean:F2}，≥200像素 {statistics.AtOrAboveThreshold}；非产品判定"
            : display.Result is { Stage: ProcessingStage.Failed } failed ? $"本帧处理失败：{failed.ErrorCode}，无有效叠加"
            : "当前帧无已匹配测量结果；不显示上一帧数值或ROI。";
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        NextButton.IsEnabled = !closing && !faulted;
        ClearButton.IsEnabled = !closing;
        ProcessButton.IsEnabled = !closing && !faulted && !busy && display.Current is not null && display.Result is null;
    }
    private void OnImageClick(object sender, MouseButtonEventArgs e)
    {
        PixelText.Text = Viewer.TryPick(e.GetPosition(Viewer), out PixelHit hit, out byte value)
            ? $"原图像素中心：Column={hit.Column}, Row={hit.Row}，灰度={value}；仅整数像素选取，非亚像素检测/毫米测量。"
            : "点击在留白或无图区域，未生成有效像素位置。";
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true; if (closing) return;
        closing = true; UpdateButtons(); StatusText.Text = "停止接收新任务，等待已接收处理和UI交接结束……";
        pipeline.Complete();
        try
        {
            if (processing is not null) await processing;
            await pipeline.Completion;
        }
        catch (Exception error) { Console.Error.WriteLine($"Close drain failed: {error.GetType().Name}"); }
        source.Dispose(); display.Clear(); Viewer.Present(display);
        closeAllowed = true; Close(); closed.TrySetResult();
        if (shutdownOnClose) Application.Current.Shutdown();
    }
}
