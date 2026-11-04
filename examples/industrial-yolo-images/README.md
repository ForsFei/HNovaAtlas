# 一般图片到固定YOLOX-Nano离线推理

.NET10/x64，复用现有OpenCvSharp4/runtime.win 4.13.0.20260627和CPU ONNX Runtime1.27.0；ProjectReference及packages.lock.json固定依赖。模型使用../industrial-yolo-onnx/.local/yolox_nano.onnx，不增加下载入口。源码不包含权重、第三方图片或完整参考数组。

从仓库根目录：

```powershell
dotnet restore examples/industrial-yolo-images/IndustrialYolo.Images.csproj --locked-mode
dotnet build examples/industrial-yolo-images/IndustrialYolo.Images.csproj -c Release --no-restore
dotnet run --project examples/industrial-yolo-images/IndustrialYolo.Images.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-yolo-images/IndustrialYolo.Images.csproj -c Release --no-build -- --native-test examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-images/IndustrialYolo.Images.csproj -c Release --no-build -- --demo examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-images/IndustrialYolo.Images.csproj -c Release --no-build -- --infer examples/industrial-yolo-onnx/.local/yolox_nano.onnx "D:/images/工件.png"
```

最后一条换成可信本地PNG/JPEG。infer默认score0.1，demo/compare固定0.01，NMS0.45。退出0成功、1托管检查失败、2用法、3原生/文件/参考失败。不联网补模型、不回退假检测、不输出OK/NG。demo/测试新建bin/verification/GUID夹具，infer/compare不写用户图片。

## 文件与流程

- OpenCvImageInput：FileStream同一份字节校验/解码；Unchanged使用存储栅格，不按EXIF旋转。8位BGR或GRAY2BGR；alpha/16位明确拒绝。只接受PNG/JPEG签名，编码文件3bytes..16MiB、解码边长1..8192且总像素<=16777216。尺寸检查在解码后，不是对不可信上传的内存防护。
- CopyBgr逐行复制非连续ROI，ToMat返回调用者拥有的Mat；Prepare中的Mat/ROI全部using，返回自有托管像素与输入。
- ImageGeometry：r=min(416/W,416/H)，int截断、零边拒绝、INTER_LINEAR、左上/114、BGR0..255 NCHW。记录理想r和实际栅格比例；641×359→416×232。
- ImagePostprocessor：共享DecodeValues，严格score筛选→除理想r→原图参考空间double/+1跨类NMS→实际内容裁剪→可见原图框。+1不具有比例缩放不变性，旧r=1后处理不能直接先NMS再除r。
- PreparedImage.Tensor.SourceHeight是完整画布416，原图几何在Geometry；raw与prepared同图关联由调用者负责。顺序Run/Dispose，无并发、硬取消、有界采集或长期吞吐承诺。
- 可见裁剪是本地显示策略：与实际缩放区域求交再/r，允许Original=null，保留审计记录。它不改变NMS参考框，不代表上游额外裁剪。

## 可选Python完整参考

本机已安装NumPy2.5.0/ORT1.27.0；项目.local虚拟环境使用system-site-packages复用，单独安装opencv-python-headless4.13.0.92（实际cv2 4.13.0），不改全局Python。C#无需Python。缺少既有包时在独立环境安装固定版本。

```powershell
python -m venv --system-site-packages examples/industrial-yolo-images/.local/python-ref
& examples/industrial-yolo-images/.local/python-ref/Scripts/python.exe -m pip install --no-deps opencv-python-headless==4.13.0.92
$imagePath = "替换为demo打印的IMAGE完整路径"
$referencePrefix = "examples/industrial-yolo-images/.local/reference-v1"
& examples/industrial-yolo-images/.local/python-ref/Scripts/python.exe examples/industrial-yolo-images/reference_images.py examples/industrial-yolo-onnx/.local/yolox_nano.onnx $imagePath $referencePrefix
dotnet run --project examples/industrial-yolo-images/IndustrialYolo.Images.csproj -c Release --no-build -- --compare examples/industrial-yolo-onnx/.local/yolox_nano.onnx $imagePath $referencePrefix
```

输出.input.f32/.output.f32/.json必须是新路径。输入519168值严格一致，原始301665值容忍1e-5+1e-4*abs(ref)；JSON限2MiB/深度16、严格字段/来源几何核对，比较3549行原图参考xyxy/class/score、候选、保留顺序与可见裁剪。坐标容忍1e-4+2e-5*abs(ref)，分数同原始值。来源记录不是签名。

## 实测（2026-10-28）

Release零警告零错误、locked restore通过。35项托管与30项实际OpenCV/CPU检查通过，既有271项YOLO回归通过，合计336项；完整参考另列。自有641×359图tensor SHA256 E4964C41C78FA76F8EF08D8AE0120C600209788B12F040140BF05F62BD750907，输入完全一致，raw最大差0，完整解码/源框最大差9.404695947523578E-05px。32候选/5保留/5可见，保留行3439/3468/3462/3454/3420。默认0.1为空。

这是渐变图数值链路，同OpenCV/ORT后端的跨语言检查；不表示5个真实目标、独立引擎、准确率、物理精度或长期性能。真实标注评价、WPF、相机/GPU、EXIF/ICC、现场部署待后续。

文章：
- /knowledge/industrial/host-computer/yolo/image-decoding-model-letterbox-and-source-space-nms/
- /knowledge/industrial/host-computer/yolo/csharp-opencv-image-files-resize-and-yolox-inference/

第六批[WPF查看器](../industrial-yolo-wpf/README.md)已交付：原图/源框共享Uniform映射、冻结图片、切图/清空/关闭结果隔离；38项WPF/手写与10项真实原生检查。后续长寿命会话/有界处理；人工UI、真实质量与现场继续待验收。
