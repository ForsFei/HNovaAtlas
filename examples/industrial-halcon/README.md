# HALCON 24.11：真实.NET接口与原生执行边界

用户选择先按24.11。项目固定MVTec官方`MVTec.HalconDotNet` **24113.0.0**（24.11.3）及packages.lock.json，.NET10/x64。接口包目标.NET Standard2.0，编译可用不代表全部环境经厂商认证。没有安装HALCON运行库或修改许可/系统PATH/注册表；厂商DLL不提交仓库。

```powershell
dotnet restore examples/industrial-halcon/IndustrialHalcon.csproj --locked-mode
dotnet build examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-restore
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release -- --contract-test
```

34项纯C#输出边界检查已通过，不调用HALCON算子。包括HTuple数组有效前缀/独立副本、长度与图标对象数一致、空输出、只读集合、有限/整数面积、行列范围与上限。这些不是分割、内存或许可验收。

匹配24.11.3原生运行库、x64及合法许可的厂商配置环境中再运行：

```powershell
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release -- --native-demo
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release -- --native-test
```

本轮真实接口Release编译零警告零错误；native-demo尝试结果为DllNotFoundException/退出3，没有原生算子成功记录，native-test两组数值尚未验证。不自动回退或吞成NoCandidates。只在常见路径找到空的HALCON-20.11-Steady目录，且有限范围搜索未找到运行DLL；不声称全机没有任何SDK，也未读取/改写许可文件。

## 文件与约束

- HalconApiDemo.cs：真正HOperatorSet链；读取neighborhood/max_connection，不改变全局状态。隔离进程要求8邻域及max_connection=0，不提供多算法并发配置锁。
- BlobReadback.cs：纯托管数组→DTO，不实现HALCON。按真实CountObj和tuple长度对应，0对象才是NoCandidates，异常独立失败。
- ContractChecks.cs：34项合成数组检查；原有--contract-test、--native-demo、--native-test三个入口保留。
- SegmentationRecipe.cs / SegmentationChecks.cs：ROI/阈值/核/面积的27项纯托管参数检查，不读取图像或调用HALCON。
- SegmentationDemo.cs：新增真实ROI分割链与独立原生入口，使用本目录16×16原创夹具，不更改旧8×6案例。

首批复用原创8×6 P2夹具，读图后要求一个图像/单通道byte/固定尺寸；不是任意文件服务或生产算法。原生输入读文件大小限1MiB，DTO图像≤1,048,576像素/对象≤64，均教学策略。输出仅像素面积及Row/Column，无毫米、产品OK/NG、帧/工件信封或机械输出。

Threshold→Connection→SelectShape(area 2..10)→AreaCenter。200..255预期Threshold对象1/连通对象2/选中1/面积4/中心row=column=1.5；254..255只剩面积1白点，筛选后预期NoCandidates。均手算预期，未用本机HALCON原生执行验证。

HObject/HTuple局部所有权finally归还，不返回原生包装器；TupleReal后DArr按Length复制，因为固定包XML说明内部数组容量可能超过有效元素数。记录这一点不代表纯数值HTuple.Dispose会清除其double数组：该版本Dispose文档明确处理存储的句柄，纯数值行为不同；实际句柄/别名和显式Clear需按对象契约设计。

## 第二批：ROI、形态学与多特征筛选

```powershell
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --segmentation-contract-test
# 下面两条需要匹配的原生库、x64与合法许可；当前未通过。
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --segmentation-native-demo
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --segmentation-native-test
```

默认原图ROI=(2,2)..(13,13)，包含端点；Threshold200..255→OpeningRectangle1(3,3)→可选FillUp→Intersection(ROI)→Connection→SelectShape(area40..60,width/height3..10,and)→AreaCenter。不填孔时CopyObj保留只读别名，不是深复制。输入固定16×16/byte/单通道，文件≤1MiB；只读核对neighborhood=8、max_connection=0、三个空对象/区域设置均true，不改全局配置，也不提供并发配置仲裁。

fixture为7×7亮块中心缺一像素，ROI内一孤点及ROI外白点。手算预期：默认阶段像素49→48→49→49，选中面积49/中心(7,7)；不填孔选中48；核1×1阶段49→49→50→50，连通块2/选中1面积49；阈值230..255在ROI内为空，后续跳过。四组断言只有--segmentation-native-test真正执行算子后才有意义，不假装已通过。

本批真实Release接口编译零警告零错误；27项新参数检查和34项旧输出检查通过。新原生test实际尝试仍为DllNotFoundException/退出3，四组分割/形态学数值未通过。无自动回退；阶段面积是处理后的掩膜像素，不是原始缺陷面积、毫米或产品判定。无触边拒绝、数据集效果、帧/工件身份接入。

## 第三批：NCC定位与模型生命周期

```powershell
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --ncc-contract-test
# 下面两条需要匹配的原生运行库、x64与合法许可，当前未通过。
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --ncc-native-demo
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --ncc-native-test
```

NccFixture生成原创64×64/byte像素，模板ROI行12..35/列10..41（24×32）内含确定性纹理。原生链GenImageConst→SetGrayval只写新独占图像→ReduceDomain→CreateNccModel(1层,角度0..0,步长0.05,use_polarity)→SetNccModelOrigin(0,0相对重心偏移)→多次FindNccModel→四元组读回。查找固定score≥0.99、最多2项、MaxOverlap0.5、SubPixel=false、1层；没有旋转/缩放案例或NCC托管回退。

模型由Run单独拥有，Find只借用；finally用固定包HTuple.Dispose归还存储句柄，不再强制ClearNccModel，没有共享/缓存/并发。结果是RawRowPixels/RawColumnPixels/AngleRadians/Score，不是准确变换像素点、毫米或机械坐标。适配器只接受本例64×64/零原点偏移/零旋转条件，不泛化为HALCON所有模型规则。

新增35项托管检查通过，涵盖四元组长度/空/上限、只读快照、分数/有限性、固定坐标/角度、像素布局与平移关系；不计算相关搜索。真实Release编译零警告零错误，既有34＋27项回归通过。--ncc-native-test真实尝试仍DllNotFoundException/退出3，四组原生断言未通过，模型释放/内存也未原生验收。

四组待验证预期：平移(18,14)后原始位姿接近(41.5,39.5)，原图接近(23.5,25.5)；小搜索ROI(40,38)..(43,41)仍找到平移目标，角落ROI(0,0)..(5,5)无匹配。前三组每坐标允许0.75像素误差，仅教学断言，非厂商精度。NoMatch不代表图中无物体，Candidates不证明唯一或产品OK；不写模型文件或发送设备指令。

## 第四批：形状匹配、旋转与ROI跟随

```powershell
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --shape-contract-test
# 原生运行库/x64/合法许可仍需匹配；以下当前未通过。
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --shape-native-demo
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --shape-native-test
```

ShapeFixture生成96×96原创F形，背景16/标记220，亮像素532；模型ROI=(24,24)..(63,63)，检测ROI=(46,42)..(48,46)面积15/中心(47,44)。平移(4,6)后理想中心(51,50)，整数+90°旋转后再平移为(55,53)；另有纯背景与双目标（1064亮像素）。这些只是夹具定义与待验证预期，不是原生实测。

ShapeDemo真实CreateShapeModel/FindShapeModel、GetShapeModelContours、VectorAngleToRigid、HomMat2dInvert/Compose、AffineTransContourXld/Region、AreaCenter/Intensity。创建/查找固定1层、角度-π/2-0.02至+π/2+0.02、步长π/180、Optimization=none、use_polarity、Contrast30/MinContrast10；查找score≥0.85、最多2项、MaxOverlap0.3、least_squares、Greediness0。参数只服务本夹具，无缩放/形变/任意角度效果验收。

参考图自匹配建立H参考；模型归零轮廓用H当前，参考原图ROI用H当前×inverse(H参考)。PixelRigidSnapshot仅验证/复制六元素刚体矩阵并按中心/角点约定映射点，不搜索或栅格化。实际ROI先角点中心守卫，再检查变换区域与当前domain交集是否相等；不完整拒绝，不静默裁掉继续统计。模型Run独占，Follow只借用，自己的矩阵/轮廓/区域finally释放；model使用固定包HTuple.Dispose归还句柄，不强制Clear共享模型。

53项纯托管检查通过；既有34＋27＋35项回归、锁模式还原与Release真实接口编译通过，零警告零错误。--shape-native-test实际尝试仍DllNotFoundException/退出3，五组原生匹配/ROI/释放未通过。前三组原生断言容差为角度0.04弧度、中心每轴1像素、面积12..18、均值≥200且有轮廓，仅教学预期；背景必须NoMatch，双目标必须Ambiguous且无ROI。原生越界样本未验证，仅有托管角点越界检查。Intensity按传入区域统计，不依赖reduced domain自动保护。

第四批结果NoMatch/Ambiguous/RoiOutsideImage/RoiPrepared及只读数值，不复用上帧ROI、不输出产品OK/NG、毫米或机械目标。

## 第五批：一维边缘、配对与像素宽度

```powershell
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --measure-contract-test
# 原生运行库/x64/合法许可需匹配；以下当前未通过。
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --measure-native-demo
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --measure-native-test
```

MeasureFixture原创128×96图：背景16/亮条220，行16..79/列48..79；另有暗槽、双条、纯背景、单阶跃与横条。默认窗口中心(48,64)、Phi0、半长度40/8，Sigma1/幅值Threshold20，bilinear；整数中心及完整窗口外1像素余量，受限Sigma/长度比等为教学策略。真实GenMeasureRectangle2→MeasurePos(all,all)→MeasurePairs(positive或negative,all)，同一次Analyze复用同一新图与句柄，之后Dispose；无跨帧缓存、定位输出接入或修改系统设置。

MeasureContracts区分N条边的N-1相邻距离、M对端点的M个Intra与M-1个Inter，检查有限/轴向位置/顺序、幅值符号/门槛、坐标距离及两次输出端点对应；数值对应不等于证明同帧。只在恰好2条边/1对时返回WidthMeasured及像素宽度，否则NoEdges/NoPair/Ambiguous且宽度null，不补0或挑第一组。输出独立只读快照，不返回原生包装器；HMeasure/CloseMeasure/MeasureProjection作接口导读，主链静态HTuple风格，固定包Dispose归还测量句柄，不强制Close借用对象。

56项新增纯托管参数/夹具/输出检查通过，既有34＋27＋35＋53项回归、锁模式还原与Release真实接口编译通过，零警告零错误。--measure-native-test真实尝试仍DllNotFoundException/退出3，七组原生数值/资源释放未通过；不执行自写边缘检测或托管回退。亮/暗条及朝上扫描横条预期32px；背景无边、单阶跃无对、亮条要求负极性无对、双条宽16/16且间隙24/不选单一宽度。原生位置/距离0.75px仅教学断言，非精度承诺。

MeasurePos/MeasureProjection忽略图像domain，测量范围必须由测量对象本身更新；没有把上一批HRegion直接当测量句柄。当前仅固定独立案例，没有相机/帧工件身份、形状定位自动对齐、毫米标定、公差/产品判定或设备动作。没有HALCON窗口、HDevEngine、WPF新集成、自动重试、安装/激活。二维Metrology见第六批；原生数值、HDevelop对照、长期内存、性能与工艺质量仍待验收。

## 第六批：直线、圆与二维Metrology

```powershell
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --metrology-contract-test
# 原生运行库/x64/合法许可需匹配；以下当前未通过。
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --metrology-native-demo
dotnet run --project examples/industrial-halcon/IndustrialHalcon.csproj -c Release --no-build -- --metrology-native-test
```

MetrologyFixture原创128×128：列0..39暗16、40..127亮220，再加入圆心(64,88)/半径20暗圆盘（1257像素）；另有只有线、只有圆、背景和半径8小圆。理想直线Column39.5、圆直径40px、圆心到无限直线48.5px、signed clearance28.5px，只是定义/算术预期，不是原生实测。

MetrologyDemo真实CreateMetrologyModel→SetMetrologyModelImageSize(先于加对象)→AddMetrologyObjectLineMeasure/AddMetrologyObjectCircleMeasure→SetMetrologyObjectParam→ApplyMetrologyModel→逐对象GetMetrologyObjectResult(all实例, result_type/all_param及score)→GetMetrologyObjectMeasures/ResultContour。直线近似(24,40)..(104,40)、半长度6/3，圆(64,88)/R20、半长度6/2，Sigma1/幅值Threshold20；卡尺20/32、实例上限2、all/all、bilinear、min_score0.8、distance_threshold1.5、迭代500、种子42，均为夹具教学配置。没有自动调参、自写找边/拟合或托管回退。

MetrologyContracts按4/3参数步长读回直线/圆，Score逐实例对应；区分模型句柄、对象Index和实例，不假定Index固定0/1。输出独立只读快照，限定128×128未标定/完整圆夹具，半径<=48/Index<=31等不是HALCON通用范围。缺失返回NoResult/MissingObject，多实例返回Ambiguous；仅各1实例时发布DimensionsMeasured，且不是产品OK。直径2R、圆心到无限直线垂距d及d−R；保留负净距，不夹到有限线段或取绝对值。

诊断保留采样框图形数、候选边缘点和结果轮廓数，不混成内点数量或精度。未查询used_edges/未计算内点RMS；轮廓Resolution1.0不是精度指标。每次Analyze独占新图与新模型，局部输出finally释放，模型由固定包HTuple.Dispose归还存储句柄，不再强制ClearMetrologyModel；Index只是数字。无跨帧复用、共享/并发、定位对齐、标定、模型文件、WPF/设备派发或身份信封。

锁模式还原、Release真实接口编译零警告零错误，新增58项纯托管输出/几何/像素检查及既有205项回归通过。--metrology-native-test实际尝试DllNotFoundException/退出3，五组原生预期未通过；未安装/修改运行环境或许可。正常图原生断言为线端点列/圆心每轴/R误差<=1px、直径/垂距<=2px、净距<=3px并有轮廓，其余四场景保留缺失/空状态；多实例仅人工输出契约验证，无原生多目标/残差/质量验收。原生资源释放、HDevelop对照、长期性能与物理精度继续待验证，下一批HDevelop导出/HDevEngine，之后WPF集成。

## 第七批：过程接口与HDevEngine

新增EngineDemo/EngineContracts和手写procedures/atlas_blob_v1.hdvp，csproj复制到输出目录。20项托管输入/版本/输出契约检查通过，不解析HDevelop；真实HDevEngine/Procedure/ProcedureCall、传图标/控制参数、Execute及两类输出接口编译通过。--engine-contract-test、--engine-native-demo、--engine-native-test分别运行契约、单例及原生对照路径。引擎搜索路径固定输出目录，不修改系统PATH，不启动调试服务器或加载任意外部脚本。

过程输入8×6 byte单通道Image和Low，输出Selected、ContractVersion=1、Areas/Rows/Columns；区域面积2..10，空结果不补零。call释放前输入图像保持存活，输出先复制为只读BlobReport再释放。native-test计划Low200/254与直接HalconApiDemo比较面积4/中心(1.5,1.5)及无候选；实际DllNotFoundException/退出3，过程文件的HALCON加载、语法、执行和释放未验证。通用XML可读取不等于HALCON格式验收；没有实际HDevelop导出、热更新、WPF/设备接入、许可修改或原生回退。下一批WPF集成，原生对照与长期内存继续单列。

## 教程

第八批WPF示例位于../industrial-halcon-wpf/，引用本项目。EngineDemo新增AnalyzeImage(HObject,double)同步借用入口，保留原文件入口（先验证Low、再读图，外层释放image）；借用入口只释放自己的call/输出包装，不清理调用者图像。WPF后台用当前捕获像素建立独占图像，调用后归还，26项会话/位图检查与编译通过，原生桥接仍DllNotFoundException/退出3。没有回退，人工UI和原生释放未验收；HALCON基础教学主线已交付，下一步OpenCvSharp。

- `/knowledge/industrial/host-computer/halcon/wpf-background-execution-frame-results-and-shutdown/`
- `/knowledge/industrial/host-computer/halcon/csharp-wpf-halcon-frame-session-and-viewer/`

- `/knowledge/industrial/host-computer/halcon/hdevelop-procedure-contracts-and-export-options/`
- `/knowledge/industrial/host-computer/halcon/csharp-hdevengine-procedure-call-and-lifetime/`

- `/knowledge/industrial/host-computer/halcon/iconic-objects-tuples-domains-and-operators/`
- `/knowledge/industrial/host-computer/halcon/csharp-first-pipeline-and-resource-lifetime/`
- `/knowledge/industrial/host-computer/halcon/threshold-roi-morphology-and-region-selection/`
- `/knowledge/industrial/host-computer/halcon/csharp-roi-segmentation-and-stage-readback/`
- `/knowledge/industrial/host-computer/halcon/ncc-matching-models-origins-and-search/`
- `/knowledge/industrial/host-computer/halcon/csharp-ncc-model-search-and-lifetime/`
- `/knowledge/industrial/host-computer/halcon/shape-matching-rotation-and-roi-following/`
- `/knowledge/industrial/host-computer/halcon/csharp-shape-model-and-roi-following/`
- `/knowledge/industrial/host-computer/halcon/one-dimensional-edges-pairs-and-measurement/`
- `/knowledge/industrial/host-computer/halcon/csharp-measure-objects-edge-pairs-and-width/`
- `/knowledge/industrial/host-computer/halcon/lines-circles-and-metrology-models/`
- `/knowledge/industrial/host-computer/halcon/csharp-metrology-model-lines-circles-and-results/`

主要依据：[官方接口包](https://www.nuget.org/packages/MVTec.HalconDotNet/24113.0.0)、MVTec 24.11算子参考，以及该固定包自带XML说明。NuGet还原只获取接口，不等于完整原生环境/许可。
