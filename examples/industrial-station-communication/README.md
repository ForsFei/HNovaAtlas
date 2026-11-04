# 工位MC通信适配：真实TCP本机回环

.NET10 / McpX0.13.0；引用industrial-station，链接已有industrial-handshake协议/客户端/模拟器及MC报文源码。仅127.0.0.1随机端口，无真实设备地址入口。

```powershell
dotnet restore examples/industrial-station-communication/IndustrialStation.Communication.csproj --locked-mode
dotnet build examples/industrial-station-communication/IndustrialStation.Communication.csproj -c Release --no-restore
dotnet run --project examples/industrial-station-communication/IndustrialStation.Communication.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-communication/IndustrialStation.Communication.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2。临时文件保留并打印路径，不覆盖用户目录。没有PLC/雷赛/相机/模型或现场安全功能。

## 对象

- CommunicationStationPort实现IStationAsyncPort，只将Move映射为MC邮箱任务，其余三步交给RecipeSimulationPort。绑定原StationCommand与Ticket，重入同操作不再提交；EpochChanged/NotRetained/未知返回null，不补造终态。
- CommunicationStationSession是推荐门面：Start、SaveResult、Reconcile、ResetObservedControl、ReleaseRemoteAfterSaved、ObserveRelease、SimulateReviewAndRelease、RequestClose/CloseStep。远端槽未证明释放时，禁止新启动和本地放行。
- StationApplication.OpenWithPortAsync取得端口所有权，磁盘意图先保存再Start；打开失败或正常收尾负责Dispose端口。

## 时序

1. 应用保存Began，再派发Move。MC暂存完整Ticket并提交1一次，匹配Done才形成Move成功；Rejected/Faulted是已结业务失败。
2. 丢提交回执：宿主故障保留Pending，显式Reconcile只读查询原Ticket，迟到终态只排空，不改原故障/不继续采图。
3. 保存本地TerminalSaved后消费，必要时明确检查并写0归零控制通道，然后写2释放远端保留结果。
4. 释放回执丢失不重写2。ObserveRelease检查同BootId/RequestId/HighWater的Idle+Released证据；仍保持2/2时显式归零，读回0/0才允许新件。
5. 显式模拟Review/本地放行，关闭等待实际Task与本地义务，之后释放本机服务/扫描任务。

Target300/600仅固定参数示意，没有实际位移单位；Ticket.Version=1是邮箱协议版本，不等于RecipeVersion。工件/配方/GUID未全部上传，只由本地绑定关联。

## 验证和限制

Release零警告零错误，51项实际MC TCP回环集成检查通过；原215工位/WPF检查及旧邮箱55检查分别回归通过。含正常双周期NG/OK、已知拒绝/故障、丢提交/释放回复、只读无写入、启动代变化、结果缺失、阶段超时和关闭。demo丢提交ACK后查询同一Ticket，submit=1/execute=1/release=1，原Faulted不改，最终Closed。

映射仅在内存，未持久化Ticket或恢复跨进程Task。模拟BootId在新对象从1开始，不能当可信设备启动代；计数只是测试诊断。本机模拟器随Dispose结束不等于真实PLC被关闭或动作停止。默认WPF仍纯模拟，本入口只有控制台；实际PLC数据契约/扫描原子性/多发布者仲裁/长期运行/现场安全待验收。

教程：

- /knowledge/industrial/host-computer/station/communication-task-mapping-and-readonly-reconciliation/
- /knowledge/industrial/host-computer/station/csharp-mc-station-port-and-loopback-session/
