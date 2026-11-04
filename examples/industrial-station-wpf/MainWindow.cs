using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace IndustrialStation.Wpf;

public sealed class MainWindow : Window
{
    private readonly TextBox workpiece = new() { Text = "part-001", MaxLength = 64, MinWidth = 180, Margin = new(0, 4, 16, 4) };
    private readonly ComboBox recipe = new() { MinWidth = 200, Margin = new(0, 4, 0, 4), DisplayMemberPath = nameof(StationRecipe.Version) };
    private readonly ComboBox peer = new() { MinWidth = 200, Margin = new(0, 4, 12, 4) };
    private readonly TextBlock stateText = Text(18), resultText = Text(15), recordsText = Text(13), errorText = Text(13), pathText = Text(12);
    private readonly Button start = Button("启动模拟"), stop = Button("流程停止"), save = Button("保存结果"), reconcile = Button("只读核对"), review = Button("模拟核对并放行"), close = Button("关闭");
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
    private StationApplication? application;
    private bool tickActive, closeWanted, allowClose, initializationFailed, closeFailed;
    private string? interactionError;
    internal StationApplication? ApplicationModel => application;
    internal string StatusText => stateText.Text;
    internal bool StartEnabled => start.IsEnabled;
    public MainWindow(string directory) : this()
    {
        pathText.Text = directory;
        Loaded += async (_, _) =>
        {
            try
            {
                application = await StationApplication.OpenAsync(directory);
                if (closeWanted) application.RequestClose();
                timer.Start(); Refresh();
            }
            catch (Exception failure) { initializationFailed = true; interactionError = failure.Message; Refresh(); }
        };
    }
    internal MainWindow(StationApplication application) : this() { this.application = application; Refresh(); }
    private MainWindow()
    {
        Title = "HNova Atlas · 离线综合工位"; Width = 1000; Height = 760; MinWidth = 840; MinHeight = 660;
        Background = Brushes.White; FontFamily = new("Microsoft YaHei UI"); FontSize = 14;
        StackPanel body = new() { Margin = new(28) };
        TextBlock heading = Text(26); heading.FontWeight = FontWeights.SemiBold; heading.Text = "综合检测工位 · 离线模拟"; body.Children.Add(heading);
        TextBlock hint = Text(13); hint.Text = "未连接设备。配方只影响模拟判定；流程停止不代表设备停止。"; hint.Foreground = Brushes.DimGray; body.Children.Add(hint);
        body.Children.Add(pathText);
        WrapPanel inputs = new() { Margin = new(0, 18, 0, 10) };
        StackPanel part = new(); part.Children.Add(new TextBlock { Text = "工件编号" }); part.Children.Add(workpiece); inputs.Children.Add(part);
        StackPanel version = new(); version.Children.Add(new TextBlock { Text = "下一周期配方（固定版本）" }); version.Children.Add(recipe); inputs.Children.Add(version);
        recipe.ItemsSource = StationApplication.Recipes; recipe.SelectedIndex = 0; body.Children.Add(inputs);
        WrapPanel actions = new(); foreach (Button button in new[] { start, stop, save, reconcile }) actions.Children.Add(button); body.Children.Add(actions);
        Border status = new() { Background = new SolidColorBrush(Color.FromRgb(242, 246, 249)), Padding = new(16), Margin = new(0, 16, 0, 12) };
        StackPanel lines = new(); lines.Children.Add(stateText); lines.Children.Add(resultText); status.Child = lines; body.Children.Add(status);
        TextBlock historyHeading = Text(16); historyHeading.Text = "最近周期记录"; historyHeading.FontWeight = FontWeights.SemiBold; body.Children.Add(historyHeading); body.Children.Add(recordsText);
        TextBlock reviewHint = Text(13); reviewHint.Text = "模拟核对：确认任务/待结已结束、结果已保存，并显式选择模拟对端处置。重启未知周期不会补造终态。"; body.Children.Add(reviewHint);
        WrapPanel reviews = new() { Margin = new(0, 8, 0, 8) };
        peer.ItemsSource = new[] { new PeerChoice(PeerResultDisposition.AcceptedByPeer, "对端已接收（模拟）"), new PeerChoice(PeerResultDisposition.ConfirmedNotPublished, "确认未发布（模拟）") }; peer.DisplayMemberPath = nameof(PeerChoice.Label); peer.SelectedIndex = 0; peer.ToolTip = "AcceptedByPeer：已接收；ConfirmedNotPublished：确认未发布，仅为模拟声明";
        reviews.Children.Add(peer); reviews.Children.Add(review); reviews.Children.Add(close); body.Children.Add(reviews);
        errorText.Foreground = Brushes.Firebrick; body.Children.Add(errorText);
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        start.Click += async (_, _) =>
        {
            string partNumber = workpiece.Text; string selected = ((StationRecipe)recipe.SelectedItem).Version;
            await Act(() => application!.StartAsync(partNumber, selected));
        };
        stop.Click += async (_, _) => await Act(() => application!.StopAsync());
        save.Click += async (_, _) => await Act(() => application!.SaveResultAsync());
        reconcile.Click += async (_, _) => await Act(() => application!.ReconcileAsync());
        review.Click += async (_, _) =>
        {
            PeerResultDisposition disposition = ((PeerChoice)peer.SelectedItem).Value;
            await Act(() => application!.SimulateReviewAndReleaseAsync(disposition));
        };
        close.Click += (_, _) => Close();
        Closing += OnClosing;
        Closed += (_, _) => timer.Stop();
        timer.Tick += async (_, _) => await PumpCloseAsync();
        Refresh();
    }
    private sealed record PeerChoice(PeerResultDisposition Value, string Label);
    private static TextBlock Text(double size) => new() { FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4) };
    private static Button Button(string caption) => new() { Content = caption, Padding = new(14, 7, 14, 7), Margin = new(0, 0, 10, 4) };
    private async Task Act(Func<Task> action)
    {
        interactionError = null;
        try { Task pending = action(); Refresh(); await pending; }
        catch (Exception failure) { interactionError = failure.Message; }
        Refresh();
    }
    internal void Refresh()
    {
        ApplicationSnapshot? snapshot = application?.Snapshot; bool ready = snapshot is not null && !snapshot.Busy && !snapshot.Closed;
        bool idle = ready && !snapshot!.CloseRequested && snapshot.Host.Station.Stage == StationStage.Idle && snapshot.Ledger.CanStartNewCycle;
        start.IsEnabled = idle; recipe.IsEnabled = idle; workpiece.IsEnabled = idle;
        stop.IsEnabled = ready && !snapshot!.CloseRequested && snapshot.Host.Station.Order is not null && snapshot.Host.Station.Terminal is null;
        save.IsEnabled = ready && snapshot!.Host.Station.Terminal is not null && !snapshot.Host.Station.TerminalConsumed;
        reconcile.IsEnabled = ready && snapshot!.Host.NeedsReconciliation;
        review.IsEnabled = ready && !snapshot!.CloseRequested && !snapshot.Host.TaskActive && snapshot.Host.Station.Pending is null &&
            (snapshot.Host.Station.Terminal is null || snapshot.Host.Station.TerminalConsumed) && snapshot.Ledger.Cycles.Any(c => c.Review is null);
        peer.IsEnabled = review.IsEnabled;
        if (snapshot is null)
        {
            stateText.Text = initializationFailed ? "记录目录打开失败" : "正在打开记录目录…";
            resultText.Text = ""; recordsText.Text = "尚未载入记录。";
        }
        else
        {
            StationSnapshot s = snapshot.Host.Station;
            pathText.Text = application!.DirectoryPath;
            stateText.Text = $"流程：{s.Stage}  ·  {(snapshot.CloseRequested ? "正在关闭" : snapshot.Busy ? "正在处理请求" : "等待操作")}\n" +
                $"工件：{s.Order?.Workpiece ?? "—"}  ·  已捕获配方：{s.Order?.RecipeVersion ?? "—"}";
            resultText.Text = $"任务运行：{snapshot.Host.TaskActive}  ·  待结：{s.Pending?.Key.Step.ToString() ?? "无"}  ·  需只读核对：{snapshot.Host.NeedsReconciliation}\n" +
                $"结果：{s.Terminal?.Outcome.ToString() ?? "—"} / {s.Judgment?.ToString() ?? "—"}  ·  本地已消费：{s.TerminalConsumed}\n" +
                $"记录准入：{snapshot.Ledger.CanStartNewCycle}  ·  端口已释放：{snapshot.Host.PortDisposed}";
            recordsText.Text = snapshot.Ledger.Cycles.Count == 0 ? "暂无周期记录。" : string.Join("\n", snapshot.Ledger.Cycles.TakeLast(5).Select(c =>
                $"#{c.Order.Number}  {c.Order.Workpiece}  {c.Order.RecipeVersion}  {c.Terminal?.Outcome.ToString() ?? "Unknown"}/{c.Terminal?.Judgment?.ToString() ?? "—"}  {(c.Review is null ? "待核对" : "已核对")}"));
        }
        errorText.Text = interactionError ?? snapshot?.Error ?? (closeFailed ? "关闭收尾失败，窗口保留；检查任务和结果记录。" : "");
    }
    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (allowClose || initializationFailed) return;
        args.Cancel = true; closeWanted = true; application?.RequestClose(); timer.Start(); Refresh();
    }
    internal async Task PumpCloseAsync()
    {
        if (tickActive) return;
        tickActive = true;
        try
        {
            if (closeWanted && !closeFailed && application is not null && !application.Snapshot.Busy)
            {
                if (await application.CloseStepAsync()) { allowClose = true; timer.Stop(); Close(); }
            }
        }
        catch (Exception failure) { closeFailed = true; interactionError = "关闭收尾失败：" + failure.Message; }
        finally { tickActive = false; Refresh(); }
    }
}
