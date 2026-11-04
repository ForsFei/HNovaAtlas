# 离线端到端工位：运动到位 → 原帧检测 → 本地结果

.NET10，无新包。引用industrial-station-motion与industrial-station-vision；仅内存轴、自有P2回放与本地账本，不连接真实卡/相机/PLC/MES/网络/原生算法。

```powershell
dotnet build examples/industrial-station-integrated/IndustrialStation.Integrated.csproj -c Release
dotnet run --project examples/industrial-station-integrated/IndustrialStation.Integrated.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-integrated/IndustrialStation.Integrated.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2，唯一临时结果目录打印并保留，无设备地址或原生SDK选项。

## 运行契约

IntegratedStationSession.OpenAsync取得组合端口所有权。显式OpenMotionSimulation、EnableMotionSimulation、OpenReplay和BeginReferenceSimulation，AdvanceSimulation(.01)推进模型；周期内准备拒绝，旧账本先核对。Start仍先保存意图。

IntegratedStationPort只调用motion.ExecuteAsync(Move)，合格回复后vision.RecordQualifiedExternalMove记录同一命令/回复，不运行视觉模拟Move。后续Capture、Inspect和模拟PublishResult交给原视觉端口；业务推进仍由StationHost/状态机决定。

原步骤Task缓存防重复，错误载荷/帧拒绝。Observe只读原结果/null，不发动作或补建步骤。停止或超时后，迟到数据只结清原Pending，不触发采图/判定发布，也不改原终态。

正常保存后确认原视觉处理收据，再显式模拟核对/放行；失败Move未建立视觉周期时Ack返回false，无虚构收据。关闭等待组合Task与本地义务后分别释放两个模块；TryAdvanceSimulation在开始关闭/已释放时返回false，避免关闭泵再Tick子模块。

## 验证

Release零警告零错误；新49组合检查、原356工位/通信/运动/视觉/WPF回归，共405项通过。覆盖唯一Move、重复/冲突、错误完成证据、无合格到位不拍照、停止后显式新周期、三处迟到、关闭与旧周期重启。

Demo两周期反馈1.000/2.000mm、帧1/2、ROI均值220/75、Ok/Ng；Move2、visionFakeMoves0、handoffs2、Closed。计数只属本次模型诊断，不是设备跨进程执行证明。

默认WPF与MC入口不变。工位GUID/帧号只记录应用关联，回放不是实际曝光因果证明。图片、FrameStamp、测量明细和设备任务绑定尚未持久化；下一批结果留图与测量追溯，原生/实机/工艺/安全仍单列。

教程：

- /knowledge/industrial/host-computer/station/integrated-offline-station-motion-to-image-and-result/
- /knowledge/industrial/host-computer/station/csharp-integrated-station-routing-and-single-move-handoff/


## 第九批：原图与测量证据

新增ExportEvidence(ApplicationSnapshot)，只导出同一Completed且未确认的保留原帧/原处理结果。见../industrial-station-evidence/README.md。新工作流先留图并校验，再保存终态、确认原视觉收据；无重拍或重复处理。旧入口不变；原文未落盘描述为第八批当时范围。
