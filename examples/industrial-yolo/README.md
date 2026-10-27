# 工业YOLO：数据评价与输入预处理

这是纯.NET10、无第三方依赖的离线教学程序。第一批处理手写框JSON，第二批实际处理自有BGR小图字节；不下载权重，不读取图像文件，不加载ONNX Runtime，不提供训练/NMS/推理性能或现场质量结论。

## 第一批：数据与评价契约

```powershell
dotnet build examples/industrial-yolo/IndustrialYolo.csproj -c Release
dotnet run --project examples/industrial-yolo/IndustrialYolo.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-yolo/IndustrialYolo.csproj -c Release --no-build -- --evaluate-fixture
dotnet run --project examples/industrial-yolo/IndustrialYolo.csproj -c Release --no-build -- --evaluate-fixture examples/industrial-yolo/fixtures/handwritten-boxes.json
```

Release零警告零错误，69项检查。成功0，文件/JSON/审计/检查失败1，用法2。夹具默认从输出目录读取，CLI不写入用户文件。

- `DetectionEvaluation.cs`：数据record、严格JSON、分组/边界审计、只读分离快照、IoU与固定工作点匹配。
- `Checks.cs`：69项手写数据/契约检查，无模型或原生后端替身伪装成推理。
- `fixtures/handwritten-boxes.json`：自有教学数据；scratch/dent，四张图元数据，无真实图像。

JSON必须明确fixtureKind=handwritten-evaluation。类别按Labels的顺序，原图连续面积边界xyxy，无+1；图片范围0..Width/Height，框有限、正面积且不越界。1MiB/深度16、标签64、图256、单图真值128、总预测4096/单图256、宽高8192/总像素16777216为教学边界，不是YOLO标准。

本清单要求train/validation/test齐全；ImageId唯一，同组不得跨集合，预测只引用测试图。它不能验证组名、物理工件、真实标注质量、内容去重或模型标签对应关系。审计复制集合，但调用方不能在审计过程中并发修改输入。

按图、分数降序及原始索引稳定排序；同类别、未匹配真值中最大IoU，真值并列按索引，阈值相等接收。无NMS、crowd/ignore、AP/mAP或标准评价器兼容声明。FP的MatchedIou为空，不代表与所有真值均无交集。无预测/无真值时对应比率分母为0则返回null。

默认score=0.5、匹配IoU=0.5：TP1 FP3 FN1 Filtered1，Precision0.25 Recall0.5；重复框、背景框、负样本各贡献一个FP。降低score到0.4：TP2 FP3 FN0；提高到0.85：TP1 FP1 FN1。仅说明手写数据的规则，不可用来选择现场阈值。

文章：

- `/knowledge/industrial/host-computer/yolo/tasks-datasets-metrics-and-deployment-contracts/`
- `/knowledge/industrial/host-computer/yolo/csharp-dataset-audit-box-iou-and-evaluation/`

输入预处理/坐标往返见下面第二批；后续加入固定来源/版本/许可/导出契约的真实模型和ONNX Runtime、按该输出解码/必要后处理与参考评价、WPF/有界集成与资源验收。不承诺通用解析所有YOLO代际。

资料：Ultralytics检测/数据/导出/指标官方文档、ONNX Runtime C#官方入门、微软System.Text.Json必需构造参数文档，具体链接与适用边界见文章。

## 第二批：RGB输入数组与Letterbox几何

```powershell
dotnet build examples/industrial-yolo/IndustrialYolo.csproj -c Release
dotnet run --project examples/industrial-yolo/IndustrialYolo.csproj -c Release --no-build -- --preprocess-test
dotnet run --project examples/industrial-yolo/IndustrialYolo.csproj -c Release --no-build -- --preprocess-demo
```

新增76项纯托管预处理/几何检查，其中一项含36组往返；既有69项评价回归通过，合计145项。新增ImagePreprocessing/PreprocessingChecks/PreprocessingDemo，Program只加两个入口；项目配置/依赖无改动。成功0，输入/检查失败1，用法2，demo只打印、不写文件或开窗口。

Bgr24Image.Create复制完整正stride BGR24缓冲（长=stride*height，像素<=16777216、每边<=8192、字节<=64MiB）；不接受Mono8/Bayer/BGRA、负stride或猜测格式。ROI整数且完整在原图内。LetterboxTransform固定居中画布、可选禁止放大，每边1..2048；ToEven取整/最薄边至少1，奇数余量右/下多1；不支持auto-stride/scale-fill。

ImagePreprocessor使用half-pixel中心、双线性、ROI内复制边缘，ToEven量化到byte后/255f；BGR转RGB、连续float32 [1,3,H,W]。PreparedInput包含只读Values/Shape、独立CopyValues和原Transform；全是托管数组，无本地句柄或Dispose，但并发/调用方副本仍可能占大量内存。

几何采用本例已取整栅格边界ScaleX/ScaleY，不保证与参考模型使用的理想ratio/标签/scale_boxes口径相同，插值也不保证与OpenCV逐位相同。3x2->5x5实际内容5x3，ScaleX=5/3、ScaleY=1.5。固定模型后必须重新核对参考输入及坐标；不把本例当通用YOLO兼容层。

ToInput接收原图全局xyxy且在ROI内；Restore接收已解码输入画布xyxy（非归一化/cxcywh/原始输出），有限有序且绝对值<=1e6。先与内容求交，纯padding/仅接触边返回null，部分裁剪记录WasClipped，再去padding/除实际比例/加ROI原点并验证正面积。极小框若还原后退化则拒绝。变换仅校验尺寸，不认证帧/工件，几何裁剪不授予业务判定资格。

demo红蓝2x1->4x4，内容4x2、上/下各1，R内容行1/0.7490/0.2510/0，B反向，G0，padding114/255。ROI(100,50,320,200)在640x480源图上到640x640，原框(120,70,220,120)->(40,160,240,260)->原框；纯顶部padding框无原图面积。

- `/knowledge/industrial/host-computer/yolo/image-channels-nchw-letterbox-and-coordinate-spaces/`
- `/knowledge/industrial/host-computer/yolo/csharp-bgr-nchw-letterbox-and-box-restoration/`

第三批已在独立`../industrial-yolo-onnx/`交付真实YOLOX-Nano CPU会话、元数据与输入/原始数值对照，固定包/官方模型哈希，31项托管及17项真实CPU检查。本项目仍无第三方依赖；该模型用独立BGR0..255/r=1输入，不直接复用本节RGB教学策略。输出解析/必要NMS、一般图像、WPF、CPU/GPU对照、现场质量与长期性能仍待交付/验收。

- `/knowledge/industrial/host-computer/yolo/yolox-nano-model-contract-and-cpu-runtime/`
- `/knowledge/industrial/host-computer/yolo/csharp-onnxruntime-session-ortvalue-and-lifetime/`
