# 第三方产物与复现来源

本项目示例代码独立组织会话与受限r=1输入，不复制YOLOX完整预处理/后处理。第三方ONNX权重及原始浮点参考只放`.local/`，不随仓库分发。

## YOLOX-Nano

- 项目：Megvii-BaseDetection/YOLOX，Megvii Inc. and its affiliates。
- 官方发布：`0.1.1rc0/yolox_nano.onnx`，3,659,407字节。
- [官方ONNX权重](https://github.com/Megvii-BaseDetection/YOLOX/releases/download/0.1.1rc0/yolox_nano.onnx)。
- 本轮固定SHA256：`c789161ed43c8269fcd4e67c67eeeb4e80c622da2eb296a20bc6007bd18a0b7d`。
- [上游LICENSE（Apache-2.0）](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/LICENSE)。
- [参考预处理](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/yolox/data/data_augment.py)。
- [参考运行/后处理入口](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/demo/ONNXRuntime/onnx_inference.py)。

保留发布来源与许可入口；文件哈希是本轮复现的一致性固定值，不是上游签名或商业适用保证。正式分发前核对代码、权重、数据和使用场景的适用条款与应保留的通知。本教程没有替项目采购授权或作法律结论，也不将COCO权重当工业缺陷模型。

## ONNX Runtime

- 固定NuGet：Microsoft.ML.OnnxRuntime / Managed 1.27.0，CPU；锁文件记录间接依赖System.Numerics.Tensors9.0.0。
- [官方项目/许可证](https://github.com/microsoft/onnxruntime/blob/v1.27.0/LICENSE)。
- [官方C#接口说明](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html)。

## 数值参考

reference_yolox_nano.py使用当前已安装的Python ONNX Runtime1.27.0和NumPy2.5.0，CPU、单线程、禁用图优化；不会安装或升级环境。自有416×208 BGR渐变夹具，不含第三方图片。r=1意味着本批不需要cv2插值，不是完整官方demo移植。

完整输入SHA256：`5f598da750eae012350fc7b4b49b154c609104ee2b65a9aa3ac5d51c7c469863`。

本机Python原始float32输出SHA256：`cb9e0d4cc9b0eb405bc9c8dca65935b91803a4889376a14b423c1f2fed55aa14`，仅记录，不要求所有平台输出位级哈希一致。比较容忍为绝对误差<=1e-5+1e-4×abs(reference)。20个样点固化在NumericReference.cs，完整301665个值通过显式参考文件对照，二者都不是准确率或独立推理引擎评测。

## 第四批后处理参考（2026-10-27）

YoloXPostprocessor及reference_postprocess.py独立实现选定路线的grid/stride/exp解码、objectness乘积及class-agnostic/+1 NMS公式；参考同一上游提交的[demo_utils.py](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/yolox/utils/demo_utils.py)。不复制完整上游模块或执行其图像显示代码。

显式本地策略：候选并列优先小行号（上游默认argsort不保证此顺序）、NMS几何double、严格分数>阈值、抑制仅overlap>阈值、NMS后r=1内容裁剪。第一批连续评价IoU不加1，未改变；不宣称位级重现上游所有排序/精度边界。

从前批实际原始参考生成的JSON仅放.local；已有NumPy2.5.0，全部3549行及候选/保留/裁剪对照通过。阈值0.01/NMS0.45时36候选、保留行3453/3436/3041，最大坐标差约0.000061035px。它是明确数值策略验证，不是独立模型引擎、真实目标或质量结论。
