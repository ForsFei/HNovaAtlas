# YOLOX-Nano：真实ONNX Runtime CPU会话与后处理

独立net10.0/x64项目，固定Microsoft.ML.OnnxRuntime1.27.0（CPU）与锁文件，引用相邻industrial-yolo项目的Bgr24Image，不改变其无依赖教学策略。模型来自官方发布0.1.1rc0，来源/许可/哈希见[第三方记录](THIRD_PARTY.md)。不是Ultralytics模型或工业缺陷权重，不用scratch/dent类别表解释输出。

## 从仓库根目录运行

```powershell
dotnet restore examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj --locked-mode
dotnet build examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-restore
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --fetch-model examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --native-test examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --demo examples/industrial-yolo-onnx/.local/yolox_nano.onnx
```

只有--fetch-model访问网络，验证3,659,407字节和SHA256后写入，已有文件只校验，不覆盖；错误文件明确失败，不绕过校验。临时.part只清理本命令创建的文件。其他入口只读明确路径；不要随意加载不可信ONNX图。

31项托管检查不执行原生会话；17项真实CPU检查覆盖固定模型/实际元数据/两次Run/20个Python样点/输入不变/输出分离/关闭及实际ORT对错误名称、形状、类型的拒绝。Release零警告零错误。成功0，托管/下载失败1，用法2，原生入口失败3，无测试替身/回退/自动重试。

## 固定契约与本轮限制

- 模型SHA256：`c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d`，读取后校验同一份byte[]再加载，名称相同/形状相同的别的模型仍拒绝。
- 输入images float32 [1,3,416,416]，BGR 0..255、无/255，左上放图、其余114。与第二批RGB/居中策略不同，用独立YoloXInput类型。
- PrepareNoResize只支持宽416、高1..416，r=1，不含任意尺寸缩放/ROI/图像解码。自有416x208渐变夹具，右侧无填充、底部208行114；非现场图片。
- 输入完整SHA256：`5f598da750eae012350fc7b4b49b154c609104ee2b65a9aa3ac5d51c7c469863`，与NumPy构建的相同。
- 输出output float32 [1,3549,85]，301665个原始值；原--demo只读原始值，第四批新增后处理入口见下文。没有完整类别显示表或检测效果评价。
- CPU、线程1/1、顺序执行、禁用图优化，为数值参考固定条件，不是生产性能推荐；没有GPU/延迟/FPS/长期资源验收。
- Session/Options/RunOptions/输入OrtValue/输出集合均按using/Dispose释放，输出Span在释放前复制。全局OrtEnv不按每帧释放；这不是泄漏分析或内存平坦证明。
- 会话只支持单调用者顺序Run/Dispose，没有并发关闭、取消硬期限、后台队列、WPF或相机集成。

## 可选：完整Python数值参考

只使用已存在的Python NumPy和ONNX Runtime，不安装/升级环境，不要求生产应用安装Python。参考路径必须未存在；重复运行请更换输出名，不覆盖原文件。

```powershell
python examples/industrial-yolo-onnx/reference_yolox_nano.py examples/industrial-yolo-onnx/.local/yolox_nano.onnx examples/industrial-yolo-onnx/.local/reference-python127.f32
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --compare-reference examples/industrial-yolo-onnx/.local/yolox_nano.onnx examples/industrial-yolo-onnx/.local/reference-python127.f32
```

本机Python ORT1.27.0/NumPy2.5.0，CPU同条件，输入一致；完整301665个原始值逐项对照，maxAbsError=0。容忍abs<=1e-5+1e-4*abs(reference)，不要求不同机器位级相同。原始文件为小端float32、无头，长度严格1,206,660字节，拒绝非有限/不匹配值；显式参考文件不等于可信签名。

这是不同语言入口到同一ORT引擎的参考，不是独立推理引擎、训练框架或实际检测质量验证。r=1免插值，尚未验证一般resize与原模型坐标适配。模型和完整参考都在忽略的.local，bin/obj也忽略；不向仓库提交权重。

## 文件与文章

- ModelArtifact：限定来源、尺寸/哈希及显式下载。
- YoloXInput：独立BGR NCHW输入与自有图案。
- YoloXNanoSession：真实会话/元数据、OrtValue、Run、分离输出与Dispose。
- NumericReference / reference_yolox_nano.py：20个固化样点及全量数值对照。
- Checks：托管/原生检查独立入口。
- `/knowledge/industrial/host-computer/yolo/yolox-nano-model-contract-and-cpu-runtime/`
- `/knowledge/industrial/host-computer/yolo/csharp-onnxruntime-session-ortvalue-and-lifetime/`

第四批已加入该模型解码/分数/NMS/r=1源框，见下节；一般图像接入、参考评价和WPF后续补充。商业模型选择/现场验收另行核对。

## 第四批：解码、NMS抑制记录与源框

```powershell
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --postprocess-test
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --postprocess-native-test examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --postprocess-demo examples/industrial-yolo-onnx/.local/yolox_nano.onnx
```

新增YoloXPostprocessor/PostprocessChecks/PostprocessReference/reference_postprocess.py，无依赖/锁文件变化、模型下载或GUI更改。新增66项明确手写数据检查（不执行模型）、12项真实CPU+后处理检查。成功0，手写失败1，用法2，原生/参考失败3，无回退。

固定stride8/16/32、52/26/13网格，float32中心/指数宽高转xyxy，概率列检查0..1、不再次sigmoid；按objectness*classProbability取单个最大类、类别并列取小索引。严格Score>阈值；候选同分按小RowIndex优先，不承诺上游NumPy默认argsort并列顺序。

固定class-agnostic NMS、double几何与上游示例+1面积口径，overlap>阈值才抑制；单独命名UpstreamInclusiveNmsIou，不改变既有BoxMath连续评价IoU。每项Suppressions记录RowIndex/KeptRowIndex/Overlap。NMS后才ClipNoResize，当前仅r=1/左上/源宽416高1..416；无交集Original=null、部分裁剪WasClipped，保留审计项，Visible另计。

每行都检查有限值/概率范围/解码正面积与±1e6算术边界，异常明确失败；全部3549行只读快照，最多3549候选，无隐式TopK或截断。返回报告不借原生内存，但会分配托管对象，不作高帧率/并发/长期资源承诺。调用者负责raw与input同帧关联，尺寸检查不认证物理身份。

实际渐变夹具最高分约0.073345，默认阈值0.1为正常空结果；demo为数值展示使用0.01/NMS0.45：3549行/3513过滤/36候选/33抑制/3保留，可映射源图3项，行号3453/3436/3041。它们不是三个真实物体或工业缺陷，ClassId不套scratch/dent表，未输出OK/NG。

可选完整参考（前一节raw参考必须已存在，JSON输出必须是新路径，重复请换名）：

```powershell
python examples/industrial-yolo-onnx/reference_postprocess.py examples/industrial-yolo-onnx/.local/reference-python127.f32 examples/industrial-yolo-onnx/.local/postprocess-python-v1.json
dotnet run --project examples/industrial-yolo-onnx/IndustrialYolo.Onnx.csproj -c Release --no-build -- --compare-postprocess examples/industrial-yolo-onnx/.local/yolox_nano.onnx examples/industrial-yolo-onnx/.local/postprocess-python-v1.json
```

只用已有NumPy2.5.0，不安装或联网。JSON限2MiB/深度16、必需参数/未知字段严格检查，核对模型/输入/阈值/源高。比较全部3549行xyxy/类别/分数、候选与保留顺序、源图框/裁剪；本机maxCoordinateError=6.10351562E-05px。坐标容忍1e-4+2e-5*abs(ref)，分数1e-5+1e-4*abs(ref)，类别与行号严格一致。参考显式使用稳定并列与double +1 NMS，不宣称与上游默认所有数值边界等价；模型原始参考不是独立引擎或质量评测。

- `/knowledge/industrial/host-computer/yolo/yolox-grid-decoding-scores-nms-and-source-boxes/`
- `/knowledge/industrial/host-computer/yolo/csharp-yolox-postprocess-suppression-ledger-and-reference/`

第五批一般图片读取、模型专属缩放/填充和参考对照已在独立的[industrial-yolo-images](../industrial-yolo-images/README.md)交付，原r=1入口保留。本项目提取DecodeValues共享解码，新适配层先除r再进行原图空间加1 NMS，再裁剪可见框。后续WPF与真实样本评价；相机/工件、质量、GPU、FPS/长期资源未验收。
