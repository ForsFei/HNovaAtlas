# 固定YOLOX CPU发布清单与原生探针

net10.0/win-x64/x64，框架依赖文件夹发布；SelfContained/PublishSingleFile/PublishTrimmed显式false。复用review/images与原模型项目，没有新模型、包版本、系统安装或WPF窗口。

```powershell
dotnet restore examples/industrial-yolo-deployment/IndustrialYolo.Deployment.csproj --locked-mode
dotnet build examples/industrial-yolo-deployment/IndustrialYolo.Deployment.csproj -c Release --no-restore
dotnet run --project examples/industrial-yolo-deployment/IndustrialYolo.Deployment.csproj -c Release --no-build -- --self-test
& examples/industrial-yolo-deployment/Verify-PublishedRelease.ps1 -ModelPath examples/industrial-yolo-onnx/.local/yolox_nano.onnx
```

脚本在自己的bin/release-GUID新建完整publish目录，复制既有模型，生成release-profile.json，启动独立进程验证。只在刚创建目录内临时改配置、移动原生文件做拒绝测试并finally还原；不操作已安装程序。打印RELEASE_ROOT和PROBE_REPORT，报告保存在目录外。现有权重需已取得，不联网补齐或自动安装运行库。

## 命令与API

- --seal release-folder release-id：固定模型/配置/框架依赖runtimeconfig核对后封存新清单，不覆盖。
- --inspect release-profile.json new-report-outside.json：环境/文件/配置/模型检查，成功InventoryPassed，原生阶段NotRun。
- --probe release-profile.json new-report-outside.json：必须执行该发布目录自己的程序；再实际跑PNG编解码/Prepare/CPU模型/后处理，核对两个主要原生模块加载路径，成功ProbePassed。
- --self-test：26项自有假文件/配置/诊断检查，不加载OpenCV或模型。

实际调用例如（替换成脚本输出的目录，输出文件须未存在且父目录存在）：

```powershell
& "D:/releases/yolox-v1/IndustrialYolo.Deployment.exe" --probe "D:/releases/yolox-v1/release-profile.json" "D:/releases/yolox-v1-probe.json"
```

Seal/Inspect/Probe主体分别为ReleaseInventory和DeploymentProbe。清单JSON<=1MiB/深度16，最多512文件、每文件512MiB、累计1GiB，包含路径/长度/流式SHA；拒绝遇到的重解析链接，清单自身除外，其余缺失/未声明/改动均失败。配置解析核对同份字节哈希，模型固定长度/SHA；本地可信目录规则不是恶意篡改认证。

Environment→Profile→Inventory→Settings→Model→ExecutionRoot→CpuPipeline→NativeOrigins。失败后后续NotRun。状态/退出码：0所选模式成功（仍区分InventoryPassed/ProbePassed）、1 self-test失败、2用法、3诊断Failed且报告已写、4封存/输出等命令异常（可能无报告）。宿主启动失败、原生硬崩溃或挂起不保证进入C#处理，需外层核对进程与报告，无硬超时/取消。

Report含实际profile字节SHA、ReleaseId、OS/.NET/架构、AppBase/WorkingDirectory、阶段状态、输入指纹/输出长度/20样点/解码行数及原生路径。唯一临时文件+Flush(true)+不覆盖移动写报告，只清理成功创建的临时文件；不承诺断电事务。报告拒绝写入被封存目录。

## 实测（2026-10-28）

Release零警告零错误、locked restore；26项非原生检查+8项真实publish/独立进程场景通过。脚本包括静态成功、错误ExecutionRoot拒绝、真实探针、配置改动、原生文件缺失、报告目录保护、清单不覆盖、还原后清点。

本次44文件/119547793字节（随后重编译可能变化），.NET10.0.12；实际AppBase为发布目录，工作目录为源码目录。BGR输入SHA=5F598DA750EAE012350FC7B4B49B154C609104EE2B65A9AA3AC5D51C7C469863；301665 raw值长度/有限检查、20固化样点、3549行解码通过，默认score0.1下kept0。不是全量raw参考比较或物体质量。两个模块onnxruntime.dll/OpenCvSharpExtern.dll均来自清单对应路径。

仅当前主机发布验证，不当干净机、GPU/相机、WPF人工UI、长期资源或现场完成。框架依赖控制台需要.NET10运行时；WPF另需Windows Desktop运行环境。Windows原生依赖按对应包准备；工具不修改PATH、不安装VC/CUDA。SHA不是签名，应用启动和原生加载已涉及执行代码，不宣称安全启动。

教程：

- /knowledge/industrial/host-computer/yolo/deployment-contract-runtime-and-native-library-diagnostics/
- /knowledge/industrial/host-computer/yolo/csharp-release-inventory-publish-and-cpu-probe/

依据[Microsoft发布概述](https://learn.microsoft.com/en-us/dotnet/core/deploying/)及[ORT安装说明](https://onnxruntime.ai/docs/install/)。权重/依赖来源沿用[图片项目THIRD_PARTY](../industrial-yolo-images/THIRD_PARTY.md)，该说明复制进publish；本例只做本地暂存，不向外部分发权重或安装包。下一阶段综合工位离线流程；真实质量和目标机/现场继续单列。

综合工位首批[纯离线状态机](../industrial-station/README.md)已交付：57项状态/身份/期限与恢复检查，未调用设备/模型。后续异步端口与任务收尾；本发布自检工具保留独立入口。
