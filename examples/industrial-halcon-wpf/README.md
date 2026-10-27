# WPF HALCON单次离线调用

.NET10-windows/x64，引用../industrial-halcon及其固定24113.0.0包。三个按钮：切原图/反相、执行HALCON、清空结果。默认只显示图，不执行算法、访问相机/PLC/运动或修改原生环境/许可。没有托管算法回退。

```powershell
dotnet restore examples/industrial-halcon-wpf/IndustrialHalcon.Wpf.csproj
dotnet build examples/industrial-halcon-wpf/IndustrialHalcon.Wpf.csproj -c Release --no-restore
dotnet run --project examples/industrial-halcon-wpf/IndustrialHalcon.Wpf.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-halcon-wpf/IndustrialHalcon.Wpf.csproj -c Release --no-build
# 以下需合法、匹配的HALCON 24.11.3原生环境；当前未通过。
dotnet run --project examples/industrial-halcon-wpf/IndustrialHalcon.Wpf.csproj -c Release --no-build -- --native-test
```

26项检查通过：实际WPF Gray8位图创建/Freeze/像素读回，以及明确测试替身的单在途、版本隔离、失败分类、输出验证与关闭排空。不验证原生算法或可见窗口。Release零警告零错误；native-test实际DllNotFoundException/退出3，没有回退。过程HDVP格式/执行、HALCON内存、人工UI/多DPI、工艺质量与真实设备未验收。

会话提交捕获不可变Mono8Image及本地显示Revision。后台复制48像素→GenImageConst/SetGrayval→EngineDemo.AnalyzeImage借用图像→独立BlobReport→finally释放。引擎call释放后，原生图由外层归还。原CLI文件入口保留；固定8×6/byte/单通道/Low200。显示版不是相机帧号或工件身份，过期结果只抑制显示，不提供生产结果持久化。

仅一个在途任务，无队列。切图/清空仍允许，但不取消旧算法；旧成功/失败不覆盖新意图。关闭禁止新提交并异步等待真实任务，再排队二次Close；原生不返回时会继续等待，没有强制中断或硬关闭期限。模型/引擎不跨帧缓存，不保证线程池固定线程，也未实现多引擎路径隔离或热更新。

BitmapSource/Canvas为普通WPF，图像400×300固定显示，像素50DIP；红点只是区域中心，不是轮廓/圆拟合/产品OK。无厂商窗口控件或dev_display。

教程：

- `/knowledge/industrial/host-computer/halcon/wpf-background-execution-frame-results-and-shutdown/`
- `/knowledge/industrial/host-computer/halcon/csharp-wpf-halcon-frame-session-and-viewer/`
