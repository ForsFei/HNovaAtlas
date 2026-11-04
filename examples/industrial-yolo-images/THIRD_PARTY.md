# 来源与固定依赖

本项目自有渐变图通过OpenCV编码，不含外部照片。独立实现图片适配、几何映射和显式NMS策略，不复制完整上游模块。

- 模型沿用[ONNX项目来源记录](../industrial-yolo-onnx/THIRD_PARTY.md)：官方0.1.1rc0 YOLOX-Nano，3659407字节，SHA256 C789161ED43C8269FCD4E67C67EEEB4E80C622DA2EB296A20BC6007BD18A0B7D；缓存不分发。
- 上游固定提交6ddff4824372906469a7fae2dc3206c7aa4bbaee：[预处理](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/yolox/data/data_augment.py)、[ONNX入口](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/demo/ONNXRuntime/onnx_inference.py)、[LICENSE Apache-2.0](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/LICENSE)。本地稳定并列、double除r/NMS和额外可见裁剪有明确差别，不声称位级普遍等价。
- [OpenCvSharp](https://github.com/shimat/opencvsharp)：NuGet OpenCvSharp4/runtime.win 4.13.0.20260627，实际OpenCV4.13.0，通过既有项目引用；原项目与分发包许可信息随其包核对。
- [OpenCV4.13文档](https://docs.opencv.org/4.13.0/d4/da8/group__imgcodecs.html)：Unchanged解码、深度/通道与EXIF行为；[几何变换](https://docs.opencv.org/4.13.0/da/d54/group__imgproc__transform.html)。
- [ONNX Runtime](https://github.com/microsoft/onnxruntime/blob/v1.27.0/LICENSE)：固定CPU1.27.0，沿用原会话策略，参考用途关闭图优化与单线程。
- 可选Python参考NumPy2.5.0、onnxruntime1.27.0、[opencv-python-headless4.13.0.92](https://pypi.org/project/opencv-python-headless/4.13.0.92/)，仅.local虚拟环境安装后者。该wheel带有自己的第三方许可，部署时核对其随包通知。

同后端的跨语言数值比较不是质量、许可适用性或工业部署验收。源码不安装或修改CUDA、相机驱动、系统PATH与商业许可。
