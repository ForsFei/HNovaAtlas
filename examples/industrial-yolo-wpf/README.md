# YOLOX离线图片WPF查看器

.NET10 Windows/x64/UseWPF，ProjectReference复用industrial-yolo-images与固定OpenCvSharp4/runtime.win4.13.0.20260627、CPU ORT1.27.0。模型缓存沿用前批，不下载权重，不安装驱动。

仓库根目录运行：

```powershell
dotnet restore examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj --locked-mode
dotnet build examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-restore
dotnet run --project examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-build -- --native-test examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-build -- --open examples/industrial-yolo-onnx/.local/yolox_nano.onnx
```

只有--open启动可见窗口，可增加图片路径作为最后参数；需Windows Desktop运行环境。成功0，self-test失败1，用法2，native-test失败3。GUI错误在状态文本显示，没有模型下载/原生失败回退。

## 设计

- 三个操作：打开可信本地PNG/JPEG、检测、清空。GUI score>0.1/NMS0.45，空框正常；类别显示COCO索引，不映射工业缺陷或OK/NG。
- ImageSnapshot从同份LoadedImage.Bgr创建96DPI Bgr24 BitmapSource并Freeze；每次加载新Guid。没有让WPF重新按路径解码，不借Mat/OrtValue内存。
- DisplaySession在UI Dispatcher修改，active请求身份+selection代次隔离切图/清空/关闭；一个推理槽，Clear不释放运行槽。Show同对象也递增代次，重复/复制请求拒绝。
- OverlayResult.FromReport检查report.Prepared.Source对象身份；raw与prepared正确关联由OfflineDetector调用链负责。只保留可见源框，保留Kept与Boxes.Count差别。
- YoloViewer用共享Uniform映射居中绘制原图与源框；2DIP框线/12DIP文字，图像区域裁剪。DPI不再额外乘到源框公式，文字提供PixelsPerDip。无缩放/拖动/标注交互。
- Task.Run单次创建并释放模型会话；不是纯推理耗时或连续吞吐实现。读图最多一个，推理最多一个；关闭先失效发布，再await读取和推理，无硬取消/关闭期限。
- C#示例沿用上批图像/资源边界：仅可信本地PNG/JPEG，8位BGR/灰度；alpha/16位拒绝，解码后尺寸检查不当上传安全防护。EXIF/ICC尚未验收。

## 检查（2026-10-28）

Release零警告零错误、locked restore通过；38项WPF/手写检查、10项真实OpenCV/CPU/WPF检查通过。前者不加载模型、不调用OpenCV；仅在测试代码构造手写OverlayResult，非运行时推理回退。覆盖冻结BGR、留白/框映射、96/144DPI像素、错图/失败/空/重复/复制/清空/关闭状态及线程归属；可见窗口及其所有事件时序未人工验收。

native-test新建641×359渐变PNG，真实CPU score0.01下5个可见框，行3439/3468/3462/3454/3420；同文件重开新身份拒绝旧结果，默认0.1为空。离屏800×600 PNG打印OFFSCREEN_PNG路径，放bin/verification/GUID，不覆盖已有图片；已查看。模型会话在绘制前释放。渐变响应不是5个真实物体、准确率或缺陷证据。

后续：长寿命单所有者会话/有界后台处理、真实标注评价与部署。相机/GPU/长期性能、多显示器和人工UI继续单列。

教程：

- /knowledge/industrial/host-computer/yolo/wpf-original-image-overlay-and-result-identity/
- /knowledge/industrial/host-computer/yolo/csharp-wpf-yolox-viewer-session-and-overlay/

固定依赖、模型和参考代码来源沿用[图片项目来源](../industrial-yolo-images/THIRD_PARTY.md)。WPF行为参考[线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)、[Freezable](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/freezable-objects-overview)与[离屏编码](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-encode-a-visual-to-an-image-file)。不包含外部照片或随源码分发权重。

## 第七批：复用会话与有界后台（2026-10-28）

新增YoloInferenceWorker.cs/WorkerChecks.cs，不新增包或模型下载。原--open入口每次检测建立会话；新增--open-reused复用一次CPU会话，界面仍只含打开、检测、清空。

```powershell
dotnet run --project examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-build -- --pipeline-test
dotnet run --project examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-build -- --pipeline-native-test examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-wpf/IndustrialYolo.Wpf.csproj -c Release --no-build -- --open-reused examples/industrial-yolo-onnx/.local/yolox_nano.onnx
```

Ready表示初始化事实，Completion覆盖消费循环退出和Dispose；两者可能异常。一个逻辑消费者创建、串行Process并释放引擎，不承诺固定OS线程。容量1..8覆盖Queued+Running+Unacknowledged，Channel Wait+TryWrite即时准入、不静默丢弃；终态收到后需原对象Acknowledge，关闭不自动消费结果。Full不推进高水位；同对象AlreadyTracked复用Promise，复制编号冲突，已确认编号Stale。调用方需单调编号，事先自行大量创建图片不受该账本保护。

处理失败先关闭准入再发布当前Failed，队列接受项转WorkerStoppedBeforeRun，最后Completion异常；无自动重试/假空结果/测试引擎回退。正常Complete排空已接收项后释放；未确认终态仍保留。确认仅本地消费，不代表持久化、PLC接收或产品合格。GUI对迟到/失败结果同样明确消费，关闭等待初始化、读取、当前UI交接及工作器退出；无硬取消/关闭期限。

实测：49项门闩/账本/UI交接检查、12项实际CPU/OpenCV/WPF检查。真实会话只创建一次，3次Run；641×359自有渐变score0.01下5个相同参考行，两个结果数值相同但对象独立，终态背压及真实模型缺失失败通过。新GUI路径编译通过但不打开可见窗口；短序列不当长期内存/FPS、质量或现场验收。测试引擎只能从本程序集内部构造，公共入口固定真实CPU模型。测试成功0、pipeline-test失败1、用法2、pipeline-native-test失败3。

- /knowledge/industrial/host-computer/yolo/long-lived-session-bounded-admission-and-terminal-receipts/
- /knowledge/industrial/host-computer/yolo/csharp-yolox-worker-channel-ledger-and-drain/

依据[Microsoft Channels文档](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels)，固定模型/依赖来源仍沿用图片项目THIRD_PARTY。下一批真实样本/标注与离线评价，持续采集、GPU、持久化/PLC、人工窗口和长期资源未验收。

第八批[本地图片/标注评价CLI](../industrial-yolo-review/README.md)已交付，43项手写与12项实际CPU/OpenCV/报告检查；既有WPF与有界入口保留。工具可接用户可信本地COCO80图片/标注，合成检查不代表真实质量；下一步部署/环境检查。
