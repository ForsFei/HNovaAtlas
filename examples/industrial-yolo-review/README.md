# 本地图片/YOLO五列标注/固定工作点评价

.NET10/x64控制台，ProjectReference复用industrial-yolo-images及固定CPU ORT1.27.0、OpenCvSharp4/runtime.win4.13.0.20260627。无新包版本、权重下载、训练或WPF窗口。

仓库根目录：

```powershell
dotnet restore examples/industrial-yolo-review/IndustrialYolo.Review.csproj --locked-mode
dotnet build examples/industrial-yolo-review/IndustrialYolo.Review.csproj -c Release --no-restore
dotnet run --project examples/industrial-yolo-review/IndustrialYolo.Review.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-yolo-review/IndustrialYolo.Review.csproj -c Release --no-build -- --native-test examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-review/IndustrialYolo.Review.csproj -c Release --no-build -- --demo examples/industrial-yolo-onnx/.local/yolox_nano.onnx
dotnet run --project examples/industrial-yolo-review/IndustrialYolo.Review.csproj -c Release --no-build -- --evaluate examples/industrial-yolo-onnx/.local/yolox_nano.onnx "D:/inspection/manifest.json" "D:/inspection/new-review.json"
```

最后一条换成用户自己的可信本地数据。demo在bin/verification/GUID生成两张自有PNG、TXT、manifest和报告，打印路径；可参考其清单格式。成功0、self-test失败1、用法2、原生/文件/评价失败3，不发布部分失败报告、不覆盖旧报告。

## 清单/标注

schemaVersion=atlas-yolo-review-v1，pipelineVersion=atlas-yolox-image-v1，labelSet=coco80-yolox，modelSha256固定前批模型。必须提供datasetVersion、annotationVersion、datasetKind（synthetic-check/user-supplied）、partition（validation/test）、scoreThreshold/nmsThreshold/matchIouThreshold、cases。

每case需id/group/image/labels/width/height/imageSha256/labelsSha256。最多32图；路径为清单目录内正斜杠相对路径，拒绝跳转/绝对路径/遇到的重解析链接及末尾点空格别名，限定可信本地目录而非恶意上传隔离。Id及文件路径唯一；实际图片编码哈希和解码尺寸核对，相同编码内容跨路径拒绝，不代表近似内容或训练数据无泄漏。

TXT UTF8可有BOM，每行恰好class cx cy w h，后四项原图归一化0..1、宽高正数、转xyxy完整在图内。class是模型0..79索引，不是COCO category_id或自定义缺陷标签；空文件为明确负样本，缺失标签失败。每图<=128真值，标注<=128KiB、清单<=1MiB/深度24；严格必需/未知JSON字段检查。标注哈希对同份字节校验后生成只读快照。

图像解码沿用前批：可信PNG/JPEG，8位BGR/灰度扩展，alpha/16位拒绝，存储栅格不按EXIF旋转；解码后尺寸检查不是内存安全防护。一个会话顺序推理，不用后台队列堆积全批原图。

## 评价/报告

score严格>阈值→源空间double/+1跨类NMS→有效栅格裁剪→连续面积IoU匹配（不加1）。同类未占用真值中最大IoU，IoU>=match阈值，分数并列小row、真值并列小索引；TP/FP/FN及ExcludedPadding逐项记录。不做AP/mAP/crowd/ignore，不声称COCO评价器兼容。空分母null，汇总是计数微汇总而非逐图比率平均。

报告含模型/清单/图片/标注/输入指纹、版本、阈值、ORT/OpenCV/.NET/架构、逐图truth/预测/决策/漏检索引、类别/分组统计。版本是声明而非签名或代码提交证明。报告完整序列化后限32MiB，唯一临时文件+Flush(true)+不覆盖移动发布；不承诺断电事务或PLC接收。限制不等于全部峰值内存硬上限。

## 实测（2026-10-28）

Release零警告零错误，locked restore通过。43项手写标签/匹配/清单检查和12项真实OpenCV/CPU/报告检查通过。641×359旧输入指纹保持一致，真实5个参考行；任意手写person框与黑图空标签共得TP0/FP5/FN1。标签不是从模型输出反推；合成结果不代表真实物体或用户质量。实际报告不覆盖、尺寸/图片哈希错误及重复编码内容拒绝通过。

教程：

- /knowledge/industrial/host-computer/yolo/local-dataset-label-contracts-and-operating-point-review/
- /knowledge/industrial/host-computer/yolo/csharp-local-label-import-inference-and-review-report/

格式依据[Ultralytics检测数据格式](https://docs.ultralytics.com/datasets/detect/)，模型索引依据[固定YOLOX COCO_CLASSES](https://github.com/Megvii-BaseDetection/YOLOX/blob/6ddff4824372906469a7fae2dc3206c7aa4bbaee/yolox/data/datasets/coco_classes.py)。仅独立读取五列格式，不引入Ultralytics训练包或复制其实现。固定权重/依赖来源沿用[图片项目THIRD_PARTY](../industrial-yolo-images/THIRD_PARTY.md)，本项目不分发第三方图片或权重。下一批部署/环境与产物检查；真实标注质量、GPU/相机、长期资源和现场仍待验收。

第九批[发布清单与CPU自检](../industrial-yolo-deployment/README.md)已交付：26项非原生检查、8项实际发布/进程场景。下一阶段综合工位离线流程，真实质量/干净目标机/现场待验收。
