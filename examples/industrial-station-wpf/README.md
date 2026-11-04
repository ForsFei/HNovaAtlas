# 离线综合工位WPF窗口

纯.NET10 Windows/x64，引用industrial-station，无额外第三方依赖。不连接设备或模型。

```powershell
dotnet build examples/industrial-station-wpf/IndustrialStation.Wpf.csproj -c Release
dotnet run --project examples/industrial-station-wpf/IndustrialStation.Wpf.csproj -c Release --no-build -- --ui-test
dotnet run --project examples/industrial-station-wpf/IndustrialStation.Wpf.csproj -c Release --no-build -- --open-demo
```

--open-demo打开使用唯一临时目录的窗口；持久化重开可用：

```powershell
dotnet run --project examples/industrial-station-wpf/IndustrialStation.Wpf.csproj -c Release --no-build -- --open "C:\HNovaData\OfflineStation"
```

目录必须是专用可信本机目录，不要混放图片/截图或手动删改日志。初始化失败显示错误并允许关闭；不会跳过.tmp/坏记录或自动修复。

操作：输入工件编号，选择下一周期固定OK/NG配方，启动模拟；本周期状态显示已捕获版本。出现终态后保存结果，再选择模拟对端处置并按“模拟核对并放行”。重启的旧周期可为Unknown，只核对不补造旧终态。流程停止只锁存软件状态；只读核对不会重发动作。

MainWindow直接组合少量控件并调用StationApplication，磁盘I/O不在UI线程；DispatcherTimer每200ms读内存快照，非实时控制计时。Closing先取消，独立提出关闭，有限CloseStepAsync保存/消费并等宿主和账本收尾，完成后真正Close。未结任务继续等待，保存失败保留错误与窗口；不提供强制退出或实际安全停止。

实测：Release零警告零错误，7项WPF离屏/按钮启用/关闭检查通过，PNG已检查布局。核心新增44应用检查和原164项回归通过，共215项。UI检查不显示窗口，文件及截图保存在打印出的临时位置；尚未人工鼠标/键盘、多显示器/多DPI或长期运行验收。

教程：

- /knowledge/industrial/host-computer/station/operator-window-recipes-and-application-orchestration/
- /knowledge/industrial/host-computer/station/csharp-wpf-station-application-and-recipe-capture/
