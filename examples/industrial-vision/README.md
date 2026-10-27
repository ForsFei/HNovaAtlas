# 视觉基础：Mono8回放、单拍关联、有界处理、平面坐标与WPF

.NET 10，无第三方包、驱动、原生库或网络，不枚举/打开相机。用户选择相机通用路线，本项目不是某一品牌SDK或固件模拟器。

```powershell
dotnet build examples/industrial-vision/IndustrialVision.csproj -c Release
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --self-test
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --demo
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --capture-test
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --capture-demo
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --pipeline-test
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --pipeline-demo
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --calibration-test
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --calibration-demo
dotnet run --project examples/industrial-vision/IndustrialVision.csproj -c Release -- --display-test
```

63项离线检查，真实读取本项目原创合成的`fixtures/contrast-target.pgm`，非相机照片/校准板。图像8×6、全图均值51.3125，阈值>=200有5个像素；仅统计，不是检测准确率或曝光判定。

## 文件

- `PlainPgm.cs`：严格P2子集，maxval=255，单图及精确像素数量、注释/空白；不支持P5/PNG/JPEG或自动缩放。
- `Mono8Image.cs`：自有紧凑托管字节、逐行复制、像素、ROI深复制与区域统计。无原生资源，不需要Dispose。
- `OfflineFrameSource.cs`：同步单调用者，最多16张同尺寸夹具、一个复用缓冲、一个未归还租约；Open/Acquire/Close/Dispose仅内存生命周期。
- `FrameLease`：using归还，CopyToOwned逐行深复制。Close后源租约失效是教学策略，真实SDK的结果可能有不同生命周期。
- `VisionChecks.cs`：63项图像/缓冲确定性检查与demo。
- `SingleCaptureSession.cs / CaptureChecks.cs`：73项应用单拍关联检查与demo。
- `InspectionPipeline.cs / PipelineChecks.cs`：71项有界处理/结果收据/预览分流检查与demo。
- `PlanarCalibration.cs / CalibrationChecks.cs`：72项纯数值平面换算/ROI还原/误差检查与demo。
- `ImageDisplay.cs / DisplayChecks.cs`：54项几何/帧匹配检查；`Program.cs`只有上述九个离线CLI选项。
- `Wpf/`：Windows/.NET 10 WPF离线窗口、22项离屏位图/Dispatcher/真实XAML事件检查。

## 契约限制

仅正stride、完整Mono8；complete由外部声明，不由数组长度推断。最低字节为(height-1)*stride+width，允许最后一行无padding；只复制有效行。非Mono8、负pitch、Packed、Bayer、多平面/压缩等不实现，不能重贴标签。

图像上限1,048,576像素、文本上限1,048,576字符、padding0..64字节和原始缓冲上限8MiB均为教学资源策略，非相机规格。固定夹具随构建复制到输出目录。Crop保留相对直接父图的SourceRegion，图像坐标从0开始，层层裁剪坐标需调用者组合；软件ROI不减少相机传输。

FrameStamp为本地会话及帧号，非真实相机/触发/工件身份。旧租约不能释放新租约；不暴露可逃逸的源buffer/指针。自有图像按值复制，不代表Mat/HObject或SDK Clone也深复制。

已有单后台处理、有界任务/结果账本、纯数值三点仿射/误差与WPF离线显示。尚无实际SDK回调，没有真实采集/曝光/硬件触发、厂商参数、镜头/手眼标定或实际精度、人工UI/多DPI、长期内存/性能或算法质量验收；没有HALCON、OpenCvSharp或YOLO依赖。下一阶段HALCON对象与C#集成。

## 单拍应用观察（不是相机SDK）

SingleCaptureSession固定设备代、主机SourceSession、配置版本和期望尺寸，只支持应用SoftwareSingle；Continuous/HardwareSingle枚举只用于拒绝误用。默认startupReviewed=false，夹具直接给true不证明队列清空、旧触发/工件对账或真实身份。输入Valid/Grabbing/TriggerReady/ProcessPermit以及关联Qualified/图像Complete/IdentityQualified均默认false。

Prepare冻结请求，TakeTrigger最多取一次本地意图；RecordDispatch分离未尝试/调用确认/结果未知，不执行IO。TriggerAssociation必须由外层独立核对，测试人工声明，不从帧号/回调顺序生成关联。归一化TriggerSequence同设备代严格递增、允许跳号、可从0开始，不自行展开原生计数回绕。FrameStamp属于独立主机图像域，不是设备触发号；同源已消费帧号不能重贴触发身份用于下一件。

ObserveFrame只有匹配原关联、设备/主机代与配置、完整/合格/尺寸正确且时间有效的自有图才匹配。重复检查比较像素，需两份临时数组，线性成本且受Mono8教学上限限制，不保证实时吞吐。未关联像素不缓存或按FIFO自动配图；外层仍持有新鲜自有包时，可在建立真实关联后显式重投。

未知、缺口或失效后粘性锁定，无恢复方法。匹配晚到图只记录一份FrameStamp元数据、不存额外晚到像素，不改Unknown、不补拍/交算法。已有匹配历史仍保留，但处理前复核图像年龄；一次TakeForProcessing与匹配ReleaseCapture只移交自有图、释放采集槽，不代表算法/产品成功。LastReleased只保留最后一份匹配历史，可持有一张图，不是审计日志。

SampledAt/ReceivedAt/now须在同一主机单调时钟域，ReceivedAt重投不刷新；非设备曝光时间。默认0.25s时效和1s单拍预算均为教学值，只在调用时检查，没有独立看门狗。Ready只在准备/取意图时需true，等待本帧时false可以正常。Invalidate不Stop/Abort/Flush，也不负责SDK资源归还；图像仍按using/深复制规则管理。本单拍类不实现关联证明、真实采集、SDK线程调度、多工件触发队列或设备时钟同步；下节有界处理属于图像移交后的应用层。

## 有界处理与结果交接

InspectionPipeline固定采集OwnerSession、尺寸和ProcessingRecipe（版本/配置/ROI/阈值）；只有一个后台工作器，测量真实自有Mono8图。Capacity为1..16，限制排队＋运行＋未确认Completed/Failed总项数，不仅限制Channel。Wait模式仅TryWrite，不建每帧Task或无限WriteAsync等待者；Full不接收、不推进高水位，调用者保留原图，显式重投不是重拍。demo在Accepted后才ReleaseCapture；Full时仍持有原采集项。

TrySubmit检查输入结构，不证明设备身份/关联声明，不检查当前年龄/工艺期限。CaptureResult应来自TakeForProcessing。同Key同原对象返回AlreadyTracked/原Task；另一个对象即使字段一样也Conflict。固定Owner内接受序号递增，较小/已确认旧Key为Stale；不支持乱序多相机或并行工件。高水位无持久化，不保证跨重启去重。

Result保留请求/工件、FrameStamp、CameraEpoch、触发号和配方；Completed仅统计完成，不是产品OK。异常/空或结构非法摘要为Failed，测量清空，保留最多80字符错误类型；无取消契约，OperationCanceledException也为Failed。纯独立托管测量可继续后项，不将该策略套到损坏的原生上下文/设备故障。

Acknowledge只接受同实例原结果引用，复制/错Key/未终态/重复确认不清槽；Console打印不是持久化/PLC确认。Complete不再接收，await Completion排空，未确认结果继续保留，工作器不等第二条结果通道；算法永久不返回也无法完成，本例没有取消/硬时限。没有原生资源，不提供Dispose；调用者必须Complete并等待退出，同时交代保留结果。

LatestPreviewSlot用Interlocked.Exchange只保留最近发布的OwnedFrame；可替换未消费预览，不改变检测所有权。像素为公开不可变托管自有图，无SDK指针/Mat/HObject；“最新”不保证曝光时间顺序，也无WPF/叠加图。总进程内存、GC时机、调用者图像/Task/历史及算法临时内存不由Capacity完全限定。

71项检查使用门闩控制处理/排队，另含32次同对象并发提交一项检查；不是长期压力、帧率、算法质量或实机验收。平面坐标与误差见下节，WPF显示后续交付。

## 平面坐标与误差

PlanarAffineCalibration.FitThree只接受恰好三组有名字的像素中心↔具名平面毫米对应点，不做多点最小二乘。ImagePoint为Column向右/Row向下，PlanePoint为Xmm/Ymm；两域用不同类型。模型固定CalibrationBinding（装置/几何版本/像素域/目标平面/完整图尺寸），正反变换都要求绑定按值一致；元数据由外部维护，不证明现实设备或几何有效。

输入三角形与目标三角形尺度归一化，绝对叉积/最长边平方小于1e-4或最短边不足源1px/目标1e-6mm则拒绝。平面每轴绝对值≤1e6mm，图像沿用1,048,576像素资源上限。均为教学策略，不是精度或设备规格。TryMap/TryUnmap仅支持三角形及其数值边界（重心比较容差1e-10），不扩展到图像其余区域；反向图像边界不自动clamp。计算失败返回false/out默认值，不能把默认(0,0)当有效位置。

MeasureDistance先转换两端点；PixelPerturbationBound使用Frobenius范数×二维输入扰动半径，只给固定线性模型的保守贡献界，不包含拟合/光学/参考/机械误差，也不是置信区间。RestoreFromCrop仅还原父图ROI偏移，嵌套逐级处理，不处理缩放/镜像/旋转/去畸变或显示坐标。

Evaluate使用1..64个独立检查点，拒绝训练名称/像素位置复用及检查点重复/越界；逐点残差为预测减参考，输出毫米RMS/最大值/有符号平均值，只读列表，不重拟合或生成Pass。名称/位置检查不能证明参考真实性。所有输入参考均手工合成，不从原P2图提取角点，不把该图当标定板；demo故意加入(.03,.04)与(-.06,0)mm参考偏移，RMS约.045092mm、最大.06mm不是实测性能。

72项纯数值检查中，一项包括36个内部样本的解析映射/逆变换补充回归。无镜头内参/畸变、单应性/3D/手眼、持久化、工艺精度验收或运动派发。WPF显示见下节。

## WPF离线显示

Windows与.NET 10 Desktop Runtime/SDK：

```powershell
dotnet build examples/industrial-vision/Wpf/IndustrialVision.Wpf.csproj -c Release
dotnet run --project examples/industrial-vision/Wpf/IndustrialVision.Wpf.csproj -c Release
dotnet examples/industrial-vision/Wpf/bin/Release/net10.0-windows/IndustrialVision.Wpf.dll --self-test
```

第二条打开窗口；第三条只做离屏自动检查，不显示窗口。verification下PNG为随机前缀、CreateNew，不覆盖，位于被忽略的bin目录。核心项目排除Wpf源码；WPF项目链接必要核心源码及P2夹具，无新包或驱动。

UniformImageViewport只实现居中Uniform，以控件设备无关单位计算实际图像矩形。中心映射加0.5，ROI边界不加；点击先排除留白，再floor选整数像素单元并取原图灰度，不是连续亚像素点、毫米位置或运动目标。右/下边界排除，0/非有限/超范围布局不接受。WPF控件绘图和点击共用同一公式，不支持额外镜像/旋转/缩放/拖动变换。

FrameDisplayState为单UI所有者，Show换图清结果；TryAttach匹配完整请求、源会话/帧、设备代、原触发、流水线和完整配方，合法Completed才显示预设ROI，Failed只显示状态；首终态后冲突不覆盖。核对是应用元数据一致性，不证明真实设备/关联/像素内容/年龄。Mono8Bitmap用自有字节创建Gray8/stride=Width后Freeze；这不替代SDK归还或原生资源释放。

窗口三按钮（下一帧/处理/清空）及像素查看，无相机或机械操作。第二张为原P2图内存反相；固定ROI均值第一张220/第二张35。关联/质量/原触发字段全部为合成声明，没有复用SingleCaptureSession证明真实关联，接收时间0只用于离线结构输入。处理延迟400ms用于演示迟到，非实测速度；切帧/清空后旧结果不叠到新图，但仍作为本地演示消费并确认，不是数据库/PLC交接。

按钮await回到UI上下文并VerifyAccess；最多一个在途处理Task，不逐帧投Dispatcher。Closing首次取消，禁新操作、Complete、await处理/UI及工作器，再释放源清显示关闭，无硬终止/原生取消。单次清空只清显示不取消任务。

54项核心检查含48个像素中心一项回归；22项WPF检查含STA/冻结位图跨线程/await InvokeAsync、RenderTargetBitmap像素检查、实际XAML按钮切帧/清空和Closing排空。已查看生成的匹配/切帧/完整内容渲染图，但未显示或人工操作窗口，不当多DPI/多屏/长期性能或实机验收。通用基础教学主线已交付，现场项继续单列。

## 网站教程链接

- `/knowledge/industrial/host-computer/vision/imaging-exposure-pixels-and-acquisition/`
- `/knowledge/industrial/host-computer/vision/csharp-mono8-replay-and-buffer-lifetime/`
- `/knowledge/industrial/host-computer/vision/acquisition-sessions-triggers-and-frame-identity/`
- `/knowledge/industrial/host-computer/vision/csharp-single-capture-session-and-frame-correlation/`
- `/knowledge/industrial/host-computer/vision/bounded-processing-preview-and-result-handoff/`
- `/knowledge/industrial/host-computer/vision/csharp-bounded-inspection-and-latest-preview/`
- `/knowledge/industrial/host-computer/vision/image-coordinates-planar-calibration-and-errors/`
- `/knowledge/industrial/host-computer/vision/csharp-planar-affine-calibration-and-validation/`
- `/knowledge/industrial/host-computer/vision/wpf-image-display-coordinates-and-frame-consistency/`
- `/knowledge/industrial/host-computer/vision/csharp-wpf-offline-viewer-and-overlay/`
