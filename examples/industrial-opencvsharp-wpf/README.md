# OpenCvSharp/WPF：离线像素宽度查看器

独立.NET10 Windows/WPF、x64项目，ProjectReference复用industrial-opencvsharp，链接既有Mono8Image.cs；传递依赖固定OpenCvSharp4及OpenCvSharp4.runtime.win 4.13.0.20260627，本项目有packages.lock.json。无新第三方库，不引用HALCON，不改系统环境，不接设备。

```powershell
dotnet restore examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj --locked-mode
dotnet build examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-restore
dotnet run --project examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-build -- --native-test
# 无参数启动可见窗口，本轮未人工操作验收。
dotnet run --project examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-build
```

Release零警告零错误、锁模式还原通过。self-test36项，实际WPF位图/冻结/像素读回/Dispatcher和布局数值，加明确托管测试替身的会话检查，不执行OpenCV；native-test26项，真实Mat/Reduce/Gaussian/托管配对/Mat转位图与离屏绘制及Dispose。成功0、self-test失败1、用法2、native-test失败3，无原生失败后的回退。测试不Show可见窗口，不能记为人工UI验收。

界面只保留下一张离线图、测量、清空。4张128×96自有Mono8夹具为亮条/暗槽/双条/空白，ROI(16,16,96,64)，复用像素宽度算法。Mat自有像素在后台创建/填充、局部using归还；BitmapBridge从Mono8预览或用既有MatBasics.CopyMono8逐行深复制非连续ROI后BitmapSource.Create(Gray8)/Freeze。没有裸指针/Mat跨到UI，也不在原图画线。BitmapSource不按Mat.Dispose方式手动释放。

FrameSession单在途、无队列，捕获不可变图/配方和本地Revision；Load/Clear递增版本，迟到成功或失败不覆盖新意图。后台输出核对冻结位图格式/尺寸/像素与输入相等，复制轮廓并复算托管分组/配对，检查元数据一致性；Reverse报告先恢复正轴再复核。不是相机帧/工件身份、外部数据真实性、算法正确性或安全证明。

MeasurementViewer在一个OnRender中Uniform居中绘制图和叠加，点坐标+.5，ROI面积边界不额外+.5。青框是预设ROI，橙线仅唯一WidthMeasured的代表跨度，非物体框/产品OK。500×300视区黑边各50，ROI(100,50,300,200)，32px点(39.5,47.5)/(71.5,47.5)显示为(175,150)/(275,150)。真实离屏像素检查验证橙线、青框、留白以及换图/清空/多候选撤线；生成唯一PNG仅在忽略的bin/.../verification/，已实际查看。

CloseAsync先禁收/清空显示、await在途任务，Window.Closing取消第一次关闭，再Dispatcher排队二次Close。真实门闩检查证明使用中Mat不被UI提前释放，排空后已Dispose且不发布。没有强制取消/硬关闭期限；原生不返回会继续等待。WPF位图资源交给框架管理，不声称GC立即回收或长期内存稳定。

未验收可见窗口人工操作、多DPI、真实相机/触发工件关联、毫米/精度/质量、PLC/运动、连续采集/结果持久化/有界生产流水线或长期性能。下一批有界处理、预览与测量结果分流及资源观察。

- `/knowledge/industrial/host-computer/opencvsharp/wpf-mat-bitmap-frame-consistency-and-lifetime/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-wpf-mat-bitmap-measurement-viewer/`

## 第八批：有界测量、最新预览与资源观察

```powershell
dotnet run --project examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-build -- --pipeline-test
dotnet run --project examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-build -- --pipeline-native-test
dotnet run --project examples/industrial-opencvsharp-wpf/IndustrialOpenCvSharp.Wpf.csproj -c Release --no-build -- --pipeline-demo
```

保持原三个按钮不变。新增MeasurementPipeline/LatestMeasurementPreview/PipelineDemo与两类检查，复用内部FrameSession.Detach输出复核、SessionChecks.Fake仅测试。Channels为.NET共享框架，无新第三方包；固定配方/尺寸/版本/Producer，本地Job编号/FrameId不是实际相机工件身份。

一个Channel消费者，FullMode.Wait但用非阻塞TryWrite；容量1..8账本覆盖排队+运行+所有未确认终态，Channel空不代表容量已释放。Accepted/AlreadyTracked返回Task，同对象重复共享Task，同编号不同对象Conflict，老编号Stale；Full无新接收/Task/高水位推进。结果正常为Completed（包含NoEdges/Ambiguous等），后端/验证失败为Failed，不补零/回退。精确Session和原Terminal引用才能Acknowledge，本地确认不代表持久化/PLC Ack；Complete禁收并排空但不自动确认/删结果。

独立预览槽按发布顺序替换一张自有图，Publish/Take/Close不确认或取消测量。MatchesDisplay仅按Session/请求引用关联，匹配不保证宽度/产品合格，也不认证外部终态。demo中A亮条/B双条Accepted、C空白Full但当前预览可为C；A/B结果即使不显示也处理后确认，再由调用方显式重提未接收的离线C，正常NoEdges且无宽度。不是自动补拍/重放或新鲜度审批。

新增51项通道/WPF检查含明确托管后端替身，不执行OpenCV；16项真实原生路径检查通过，含32轮短回放实际Mat释放、单个后端范围、终态确认与容量观察。真实Mat范围内的合成故障不冒充现场故障。ProcessResourceSample读取账本、GC.GetTotalMemory(false)、当前进程PrivateMemorySize64/WorkingSet64，不强制GC、不要求RSS平坦；短回放观察到内存上涨如实保留，不作为泄漏/无泄漏、长期吞吐或质量结论。

只有内部持有数量被限制；调用方无限保存结果/Task/位图、生产者预建图和Dispatcher投递仍可能无界。未做连续采集GUI连接、实际触发/工件/时效、持久化/重启恢复/PLC回执、原生强制取消、实际精度/质量或长期性能。OpenCvSharp基础教学主线已交付，下一阶段YOLO；环境/现场与长期项目仍待验收。

- `/knowledge/industrial/host-computer/opencvsharp/bounded-measurement-preview-and-resource-observation/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-channel-ledger-receipts-and-preview-slot/`

接着进入独立`examples/industrial-yolo/`，先学习任务/数据/指标与69项手写框契约检查，不改变本WPF界面、后端或固定依赖。真实模型、输入预处理、ONNX Runtime与显示集成仍为后续交付。

- `/knowledge/industrial/host-computer/yolo/tasks-datasets-metrics-and-deployment-contracts/`
- `/knowledge/industrial/host-computer/yolo/csharp-dataset-audit-box-iou-and-evaluation/`
