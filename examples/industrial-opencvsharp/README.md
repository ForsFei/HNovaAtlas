# OpenCvSharp：Mat、区域、几何与边缘拟合

独立.NET10/x64，固定OpenCvSharp4和OpenCvSharp4.runtime.win均4.13.0.20260627及packages.lock.json。NuGet原生包仅作为项目依赖，不修改系统PATH/注册表或现有HALCON项目。复用原创8×6 P2夹具，不打开相机、HighGUI/WPF窗口或写图像文件。

```powershell
dotnet restore examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj --locked-mode
dotnet build examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-restore
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --contract-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --native-test
```

--demo与--native-test运行相同原生检查；退出0完成、1托管失败、2用法错误、3原生失败。无托管回退。真实编译零警告零错误，20项托管布局检查与26项原生Mat检查通过，实际OpenCV版本4.13.0。锁文件首次还原生成，后续锁模式复现；跨平台须另评估对应运行依赖，本例只验证本机Windows/x64。

PixelLayout限定正步长Mono8、1048576像素/4194304行距教学上限，核对末行有效范围、ROI非空/完全包含；不处理负步长、三通道或packed相机格式。MatBasics.CopyMono8检查二维CV_8UC1，缓存Rows/Cols/Step，按Ptr(row)逐行拷贝有效列；不会直接把窄ROI.Data当连续字节。这个头部推导不证明任意外部缓冲有效，调用者必须保持Mat存活且不并发写入。

实际原生验证：PGM解码6行8列、面积元素/类型/像素；2×2 ROI保留父Step8且不连续；写ROI影响父图而Clone独立；空目标CopyTo独立；OpenCV自有ROI保留数据至父头部释放后；C#赋值同一包装；BGR/灰度、16位/8位饱和与显式缩放；PNG内存编解码往返。没有直接包装相机指针、写磁盘、检测算法、长期内存/吞吐、WPF或产品精度验收。

教程：

- `/knowledge/industrial/host-computer/opencvsharp/mat-pixel-types-stride-roi-and-ownership/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-mat-creation-pixel-access-roi-and-lifetime/`

## 第二批：滤波、阈值与形态学

```powershell
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --segmentation-contract-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --segmentation-native-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --segmentation-demo
```

新增SegmentationRecipe/Fixture/Demo/Checks，包/锁文件不变。默认None平滑、Binary严格>200、3×3矩形核开运算、禁用闭运算；FilterSize3..9奇数/Sigma.1..10/MorphSize1..9奇数等是教学策略。输入借用并先Clone隔离ROI父邻域，源不改写；None/Mean/Gaussian/Median选择其一→Threshold→可选Open→可选Close，不启用时CopyTo。均值/高斯Reflect101，中值内置Replicate，形态学显式Constant0/中心锚点/iterations1，不冒充全部默认值。

返回尺寸/配方和三份0/255只读托管掩膜、前景计数；实际CountNonZero与拷贝像素核对，不携带Mat或指针。原创32×24图包含192像素亮矩形减1暗孔加1孤点，阈值192→开191→禁闭191；可选闭运算后192但掩膜与初始不同；中值配方188→188→188。闭运算会抹掉暗孔证据，不自动判断它是噪声。

新增21项纯托管配方/夹具与28项真实原生检查通过，Release零警告零错误，既有20+26项回归通过。实际覆盖Binary等号/BinaryInv/InRange、Otsu两级图阈值10、自适应MeanC均匀100图C2全白/C0全黑、脉冲均值/高斯/中值、腐蚀/膨胀/开闭、1×1核、ROI父邻域隔离、贴边Constant0/Replicate腐蚀1/9差别、输入拒绝/不改写及只读快照。Otsu/自适应只作独立示例，未接主链；无连通域/轮廓、产品判定、物理精度、相机/WPF或长期压力验收。连通域和轮廓见第三批。

- `/knowledge/industrial/host-computer/opencvsharp/smoothing-thresholds-and-morphology/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-filter-threshold-morphology-and-stage-readback/`

## 第三批：连通域、轮廓与层级

```powershell
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --regions-contract-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --regions-native-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --regions-demo
```

新增RegionContracts/RegionDemo/RegionChecks，包与锁文件不变。输入非空2D CV_8UC1且严格0/255二值且最多1048576像素，复制后ConnectedComponentsWithStats(CV_32S、4/8邻域)→统计/质心读回→FindContours(Tree/ApproxSimple、origin)→几何/只读点快照。标签0背景跳过，不读全白图的空背景质心。像素面积/框/质心校验与CountNonZero核对，默认按2..1000像素及可选触边规则筛选；保留原始Components/Contours，不建立Label与ContourIndex一一对应，不默认取最大/第一项。

18项纯托管配方/父链与24项真实原生检查通过，既有95项回归、锁模式还原及Release零警告零错误。原创32×24图像素面积91/20/1，共112；筛选2项，Tree4条/External3条；大框100、组件91、外轮廓81/周长36、孔轮廓14/周长约13.65685（实际黑孔9像素），单点轮廓面积0。另测4/8对角连通、全黑/全白、非二值拒绝、只读点、面积方向、深度0/1/2白岛与CComp两层、ApproxNone、触边、ROI/原点平移。原生标签/轮廓数值为本次局部索引，不当跟踪或产品身份。

对象/轮廓上限256、总点65536、偏移±1000000为教学资源边界，原生返回后检查，不保证峰值内存或硬实时。Parent链验证索引/循环，其他层级链接仅范围校验，不冒充完整外部图校验。结果含原始轮廓，不是筛选后掩膜的轮廓；没有自动串接上一批快照、重建筛选掩膜、孔黑像素计数、跟踪、Moments/旋转框/亚像素、标定/毫米、WPF/设备或长期质量验收。下一批几何特征、旋转框与形状筛选。

- `/knowledge/industrial/host-computer/opencvsharp/connected-components-contours-and-hierarchy/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-components-contours-hierarchy-and-readback/`

## 第四批：几何特征、旋转框与形状候选

```powershell
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --shapes-contract-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --shapes-native-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --shapes-demo
```

新增ShapeRules/ShapeDemo/ShapeChecks，包/锁文件不变。Describe面向有序简单轮廓/已知多边形，限制点数与坐标但不做通用自交检测；ContourArea/ArcLength→ConvexHull/ApproxPolyDP→MinAreaRect角点/Moments→只读特征。保护零面积/周长/凸包面积/短边/M00分母，退化返回null而非无穷或默认0方向。保留原生框Angle，另规范化长短边并从最长角点边计算图像x右/y下的无向轴[-90,90)度；180°等价，近方形ratio<=1.05方向null，不辨头尾/不当机械姿态。

16项托管规则/角度与20项真实原生检查通过，Release零警告零错误。实际覆盖矩形面积40/周长28/矩中心(5,2)、整数旋转矩形轴45°/ratio4、L形面积28/凸包46/框64、正方形方向抑制、点/线退化、平移/反序、只读角点和Tree候选。规则默认根轮廓、无直接Child、面积>=10/solidity>=.9/boxfill>=.85/aspect<=5/凸四边形；保留首个拒绝原因和特征，不对应Selected组件列表或Label。前篇夹具默认一候选，RejectChildren=false变两候选，证明外轮廓比值不能证明无孔。没有圆度标准验收、角度头尾识别、外部层级完整验证、跟踪、毫米标定、WPF/设备或长期性能；下一批边缘与线/圆拟合。

- `/knowledge/industrial/host-computer/opencvsharp/geometric-features-rotated-boxes-and-shape-selection/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-rotated-boxes-hulls-and-shape-features/`

## 第五批：边缘、候选与关联点拟合

```powershell
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --edges-contract-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --edges-native-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --edges-demo
```

新增EdgeRules/EdgeContracts/EdgeDemo/EdgeChecks；沿用固定包/锁文件。Detect要求非空2D CV_8UC1、最多1048576像素，同步借用并Clone隔离ROI父邻域，5×5 Gaussian(sigma1,Reflect101)→Canny(Low50/High150/aperture3/L2)→HoughLinesP(1,π/180,20,30,3)。Gradient HoughCircles另读平滑灰度(dp1/minDist32/param1=100/param2=20/R20..28)，不是Canny边缘图。结果为只读候选及边缘像素计数，各类候选64上限在原生返回后检查，不保证峰值内存/硬实时；坐标局部于输入，不自动关联目标或筛选第一项。

拟合另用已知关联点，每组最多1024、坐标有限且±1000000；FitLine至少2点，中心化后转Point2f，L2读Vx/Vy/X1/Y1并还原，输出单位方向、垂距RMS与样本投影范围，非物理端点。教学FitCircle至少6点，中心/尺度归一化后CV_64F矩阵经Cv2.Solve(SVD)求代数最小二乘，再算径向RMS；它不是Cv2.FitCircle、几何最小二乘或RANSAC。重合/近共线返回null，协方差展开门槛并非完整圆弧覆盖证明；异常点会显著偏置结果。MinEnclosingCircle仅理想点独立测试，不参与拟合实现。

新增18项纯托管和27项真实原生路径检查通过，Release零警告零错误。实际条带190边缘像素/2线/0圆；圆盘176边缘像素/0线/1圆，Hough中心(62.5,62.5)、R22.7对理想(64,64)/R24有偏差，3px只是夹具容差。已知八点实际圆心(20,30)、R约5、径向RMS约8.88e-16，非图像提边的精度证据。覆盖空白/小图/ROI/源不改写/只读、横竖斜线、投影范围、平移/缩放、重合/共线/极短圆弧和异常点。无自动提取拟合点、亚像素边缘、鲁棒剔除、标定/毫米、公差或产品OK、相机/WPF/执行机构、长期内存/吞吐验收。下一批受约束局部边缘与像素尺寸测量。

- `/knowledge/industrial/host-computer/opencvsharp/edges-hough-line-and-circle-fitting/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-canny-hough-fitline-and-circle-least-squares/`

## 第六批：局部平均轮廓与条带像素宽度

```powershell
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --stripe-contract-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --stripe-native-test
dotnet run --project examples/industrial-opencvsharp/IndustrialOpenCvSharp.csproj -c Release --no-build -- --stripe-demo
```

新增StripeRecipe/StripeAnalysis/StripeDemo/StripeContracts/StripeChecks，包及锁文件不变。输入同步借用非空二维CV_8UC1、最多1048576像素，轴对齐ROI必须完整包含、不裁剪。原生Reduce(Avg,CV_64F)沿X压行/沿Y压列，5×1/1×5 Gaussian(sigma默认1,Reflect101)后读回两份double轮廓；临时Mat局部using，不返回Mat/指针。扫描长度16..4096、平均方向1..256、sigma.25..2、差分门槛(0,255]为教学范围。

纯C#分析复制/按Reverse反转轮廓，计算相邻差分并在i+.5定位，相邻同号且绝对值>=门槛的响应加权分组；每组保留范围/带符号峰/位置，最多64组，过量异常不截断。亮条正/负，暗槽负/正，只配相邻组；Guard3端部响应优先拒绝，其后NoEdges/NoPair/Ambiguous，只有恰好两边一对才WidthMeasured，顶层宽度/端点在失败时null。诊断Pairs仍可包含未获唯一发布资格的宽度，不应直接当测量终态。

34项纯托管检查和28项真实原生路径检查通过，Release零警告零错误。托管测试不调用OpenCV，真实路径执行Reduce/Gaussian后仍由C#分组/配对，不叫62项原生卡尺测试。亮条/暗槽/横纵/反向实际32px，默认扫描23.5/55.5映射图像(39.5,31.5)/(71.5,31.5)，差分峰约±80.523989。双条带4边/2对Ambiguous，空/单边/错极性/端部不发布宽度；验证ROI/坐标/源不改写/只读/阈值/容量。半高度条带平台平均120仍测得20px，明确证明平均会隐藏材料缺失。无旋转窗口、逐行完整性、亚像素精度承诺、标定毫米、公差/产品OK、相机/WPF/PLC/机械或长期资源验收。下一批WPF显示、同帧结果及资源交接。

- `/knowledge/industrial/host-computer/opencvsharp/local-edge-profiles-polarity-and-pixel-width/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-projected-profiles-edge-pairs-and-width/`

## 第七批：独立WPF查看器

见相邻examples/industrial-opencvsharp-wpf/README.md及IndustrialOpenCvSharp.Wpf.csproj。该Windows/WPF项目引用当前算法与固定包，不修改当前CLI入口；复用像素宽度、Mono8逐行拷贝，增加冻结位图、单在途本地版本隔离、Uniform同帧叠加及关闭排空。界面仅下一图/测量/清空，36项WPF/会话检查（明确测试替身，不执行OpenCV）与26项真实OpenCV/WPF离屏/桥接/Dispose检查通过，人工可见窗口、多DPI和长期性能未验收。

- `/knowledge/industrial/host-computer/opencvsharp/wpf-mat-bitmap-frame-consistency-and-lifetime/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-wpf-mat-bitmap-measurement-viewer/`

## 第八批：有界测量与预览分流

相邻industrial-opencvsharp-wpf新增--pipeline-test/--pipeline-native-test/--pipeline-demo，不改当前算法CLI或GUI三个按钮。51项实际通道/位图与明确托管替身检查、16项真实原生路径检查通过，含32轮短回放和资源点采样；保留测量终态直到精确收据确认，预览单槽可替换，关闭排空不自动确认。计数有界不等于整个进程所有引用有界，内存上涨不作长期验收结论。OpenCvSharp基础主线已交付，继续YOLO；实际精度/质量、设备、人工UI、多DPI、持久化/PLC和长期性能仍未验收。

- `/knowledge/industrial/host-computer/opencvsharp/bounded-measurement-preview-and-resource-observation/`
- `/knowledge/industrial/host-computer/opencvsharp/csharp-channel-ledger-receipts-and-preview-slot/`

下一阶段从YOLO任务/数据/评价开始，独立`examples/industrial-yolo/`先做手写框契约检查，不改当前原生包/算法，也不把该数据评估当模型推理。

- `/knowledge/industrial/host-computer/yolo/tasks-datasets-metrics-and-deployment-contracts/`
- `/knowledge/industrial/host-computer/yolo/csharp-dataset-audit-box-iou-and-evaluation/`
