# 工位视觉适配：Mono8回放、原帧处理与结果确认

.NET10，无第三方包、网络、相机SDK或原生视觉库。引用industrial-station与industrial-vision，复用原创P2夹具、OfflineFrameSource、SingleCaptureSession、InspectionPipeline和Mono8Image。

```powershell
dotnet build examples/industrial-station-vision/IndustrialStation.Vision.csproj -c Release
dotnet run --project examples/industrial-station-vision/IndustrialStation.Vision.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-vision/IndustrialStation.Vision.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2；只在系统临时目录新建唯一结果目录，打印路径并保留。没有相机IP、SDK、模型或任意图片输入选项。

## 对象与顺序

VisionStationSession.OpenAsync取得端口所有权，显式OpenReplay。原账本未核对时先模拟核对旧周期，无需打开源，不补造旧终态。Start前仍先保存本地Began。

Capture先转移单拍意图，再Acquire借用并CopyToOwned，using立即归还，按本回放显式关联帧/触发，TakeForProcessing与ReleaseCapture转移自有像素。图像Width8/Stride10，padding238不算像素。

Inspect将原CaptureResult交给固定配方容量1处理器，核对处理器/请求/源帧/采集代/触发号/配方。ROI均值>=200且亮像素>=3才Ok；同demo-ok-v1原图均值220得到Ok，反相35得到Ng，不照搬StationRecipe.Judgment。

SaveResult先落盘再消费本地终态，AcknowledgeVisionAfterSaved才确认原处理器收据，随后模拟Review/本地放行。重复原步骤返回原Task，不重采/重算；错误帧和判定拒绝。统计异常/NaN是处理失败，不当Ng。

迟到采图只排空不触发Inspect；迟到算法即使有Ok也不改原Faulted或发布。Dispose拒绝活跃端口调用，正常关闭Complete并await工作器，再关闭源；不会伪造Acknowledge，可能保留一份未确认结果。CycleAcknowledged是业务确认，不代表托管数组立即被GC，旧单拍历史仍可能保留有限图像引用。

## 验证与边界

Release零警告零错误，新47检查、原309工位/通信/运动/WPF回归，共356项通过；旧视觉63、单拍73、流水线71另行回归。demo两帧实际ROI处理、借用归零、正常确认后保留0并关闭。检查含身份/质量错误、重复、异常/NaN、旧周期重启、迟到采图/处理和关闭。

全部图像为自有8×6合成夹具，关联和每次+0.001的回放时间是离线声明，非真实相机触发/帧龄证明。Move/PublishResult保持模拟，不与MC/单轴自动串联，默认WPF不变。没有HALCON/OpenCvSharp/YOLO、相机缓冲/原生资源、真实质量或现场验收。

工位文件只保存FrameId与终态；FrameStamp/ROI明细/图片和触发映射未持久化，不支持重启还原原图/任务。下一批组合运动与视觉端到端离线工位。

教程：

- /knowledge/industrial/host-computer/station/vision-task-owned-frames-and-inspection-handoff/
- /knowledge/industrial/host-computer/station/csharp-vision-station-port-and-frame-receipts/

## 外部Move交接（端到端组合）

新增RecordQualifiedExternalMove(command,result)，仅接收匹配的成功Move数据，不发送运动或采图。重复同内容幂等，失败/冲突/旧周期拒绝；结构检查不能独立证明真实到位，必须由拥有运动Task的组合层调用。

VisionPortSnapshot增加SimulatedMoveCalls/ExternalMoveHandoffs。原独立例子仍模拟Move；examples/industrial-station-integrated使用运动模型唯一执行Move，再交接证据，避免重复派发。旧47项检查回归通过。


## 第九批：原图与测量证据

新增ExportEvidence(ApplicationSnapshot)，只导出同一Completed且未确认的保留原帧/原处理结果。见../industrial-station-evidence/README.md。新工作流先留图并校验，再保存终态、确认原视觉收据；无重拍或重复处理。旧入口不变；原文未落盘描述为第八批当时范围。
