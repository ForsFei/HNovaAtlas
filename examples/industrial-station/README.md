# 综合工位第一批：纯离线状态与身份

纯.NET10、无第三方依赖。只创建模拟命令并接收声明式终态；不调用PLC/雷赛/机器人/相机/视觉库，不接入WPF，也没有硬件端点选项。

```powershell
dotnet build examples/industrial-station/IndustrialStation.csproj -c Release
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2。Demo打印正常NG周期、结果发布超时、迟到回执排空及显式逻辑复位；不写用户文件，不自动重试。

## 契约

- 一个周期/一份终态/最多一个待结命令，无队列。Producer+单调Number接收，当前同对象AlreadyTracked、副本同号Conflict、释放后旧号Stale。
- Session/Cycle/Operation/Step、工件/配方、帧/判定分别匹配。采图成功得到帧，检测必须同帧，结果回执必须同判定。NG是合法检测判定，正常回执后Outcome仍Completed。
- TryTakeCommand只转移一次，Snapshot.Pending不可用作重发触发器。Success=false表示已观察到终结的失败，不可用来包装底层仍在运行的等待超时。
- TimeProvider单调时间，默认预算2/1/3/1秒，达到期限即过期。Tick、Receive和取命令时检查；没有后台计时器。未取走的过期命令撤回，已取走的保持Pending。
- 超时/错帧锁存不可变终态；正确迟到回复仅DrainedLate，记录证据但不推进下一步或改成正常完成。软件Stop不证明真实动作停止，也不代替安全控制。
- ConsumeTerminal仅原本地收据对象且一次；Completed另需ReleaseCompletedCycle，故障/停止需Reset。恢复声明匹配会话/周期/锁存版本，且通信/运动/视觉/清料/结果核对/许可齐全；本地待结非空或终态未消费仍拒绝。
- OfflineSimulation.DeclaredRecovery全true仅测试声明，不可当实机观察。逻辑Reset不回零、不运动、不重发结果。关闭等待已转移命令结清及终态消费，Closed不再复位。
- 所有修改用lock串行，锁内不执行设备SDK/业务回调/await，仅采样注入的单调时钟；日志只保留最近128条只读快照，不是持久化审计或断电恢复。

## 实测（2026-10-28）

Release零警告零错误，57项确定性检查通过，包括32个并发取命令只转移一次；手动时钟，无Sleep等待。demo正常NG周期完成，发布回执超时保持Faulted，迟到Ack只排空，消费及声明复位后Idle，最后Closed。

未接设备、算法、模型或真实安全许可；无异步SDK任务、现场时序、工艺、长期资源或重启验收。异步模拟端口与单所有者宿主已在下节交付；下一批结果记录、持久化与重启核对，再接界面及通信运动视觉模块。

教程：

- /knowledge/industrial/host-computer/station/cycle-identity-states-timeouts-and-recovery/
- /knowledge/industrial/host-computer/station/csharp-offline-station-reducer-and-fault-scenarios/

计时依据[Microsoft TimeProvider](https://learn.microsoft.com/en-us/dotnet/standard/datetime/timeprovider-overview)，等待与操作结束区别见[Task.WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-10.0)。

## 第二批：异步端口与宿主

```powershell
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --host-test
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --async-demo
```

新增StationHost、IStationAsyncPort与模拟端口。宿主独占状态机，最多一个实际端口任务；ExecuteAsync执行一次，ObserveAsync只读核对。RecordUncertain锁存故障但不清待结，不把Task异常或取消当成物理动作终止。

StartAsync、PulseAsync、StopAsync、ConsumeAsync、ResetAsync、ReleaseAsync和ReconcileAsync通过有界邮箱串行处理，满载立即MailboxFull。RequestClose使用独立信号；关闭后仍允许消费与只读核对，实际任务退出且待结清除后才释放端口。Snapshot/ObserveAfterAsync提供最新快照，不是持久化事件流；Completion包含释放结果，释放异常会传播。

自动唤醒采用系统25ms延迟，状态阶段期限仍使用注入TimeProvider；不是实时控制保证。Task.Run隔开阻塞前缀与状态所有者，不提供强制取消。没有硬件SDK、自动重试、自动补发、实际停止或安全功能。

Release零警告零错误；新增48项异步/门闩检查、原57项状态机检查，共105项通过。async-demo实际观察到：等待超时后Task仍运行，关闭先等待，迟到回复仅DrainedLate，最终Execute一次、Dispose一次。测试的10秒等待上限仅防测试挂起，不是生产关闭策略。

教程：

- /knowledge/industrial/host-computer/station/async-ports-task-lifetime-and-host-draining/
- /knowledge/industrial/host-computer/station/csharp-async-host-mailbox-ports-and-reconciliation/

## 第三批：文件结果记录与重启核对

```powershell
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --ledger-test
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --ledger-demo
```

StationLedger使用专用可信本机目录，.writer.lock持有FileShare.None写锁；Began → 可选TerminalSaved → Reviewed追加发布，每条唯一.tmp写完整UTF-8 JSON、Flush(true)、关闭后File.Move不覆盖发布。最多1000事件/单条65536字节；未知/重复/缺失JSON字段、错误身份、坏顺序或残留.tmp拒绝加载，不自动修复/删除。

Begin必须先于首次命令转移，SaveTerminal必须先于本地Consume。LedgerSimulation.RunNormal演示此顺序，仅调用离线模拟；没有自动把磁盘I/O接入StationHost。API为Begin、SaveTerminal、Review、Report、DirectoryPath、Dispose；写入异常后StorageUncertain阻止新增，关闭并检查后重开。

重开报告中未Reviewed周期阻止新工件，Terminal=null保留未知；已有结果不代表物理恢复或对端接收。显式RestartReview记录核对人/证据/时间、运动/视觉空闲、物料处理与结果渠道声明，支持已接收或确认未发布；测试声明不当设备观察。Review不执行动作、不改旧结果、不复活Task或旧收据。每个Producer的Number高水位从Began重建，重复Begin的false不能授权重发。

Release零警告零错误；新增59项实际文件/重开/注入检查，原57状态机+48宿主回归，共164项通过。demo正常NG落盘后重开仍阻止，显式模拟核对后可接新意图，意图中断重开又阻止且不重放动作。新入口在系统临时目录创建唯一专用目录，打印路径并保留证据供检查。

这不是数据库、防篡改审计、可靠消息投递或真实掉电试验。没有目录刷盘、尾部删除检测、备份和签名；文件保存不与PLC/MES事务绑定。同步I/O不应放进UI线程或状态所有者循环，后续应用编排再组合异步记录与界面。

教程：

- /knowledge/industrial/host-computer/station/result-recording-durable-intent-and-restart-review/
- /knowledge/industrial/host-computer/station/csharp-file-ledger-and-restart-reconciliation/

下一批操作界面、配方与应用编排，之后分批接通信、运动和视觉；实机/工艺/安全仍单列。

## 第四批：配方与应用编排

```powershell
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --application-test
dotnet run --project examples/industrial-station/IndustrialStation.csproj -c Release --no-build -- --application-demo
```

StationApplication组合私有StationHost与StationLedger。一个应用请求槽，忙时即时ApplicationBusy，不排队；同步文件I/O在该后台操作中执行，Snapshot只读内存缓存。Idle+文件准入时先Begin，成功后才Start；关闭可独立提出，写入中关闭保留意图但不派发。

固定不可变StationRecipe目录：demo-ok-v1/demo-ng-v1，模拟判定Ok/Ng，每步100ms只用于显示节奏。WorkOrder捕获RecipeVersion；运行期不读UI选择。RecipeSimulationPort按捕获版本响应，不连接设备。

SaveResultAsync先落盘再消费原终态；SimulateReviewAndReleaseAsync检查Task/Pending/终态消费，追加明确模拟声明后放行/复位。重启旧周期仅核对，不恢复旧任务。CloseStepAsync是有限关闭步骤：保存+消费，等实际任务和宿主Completion，再释放账本；没有强制关闭时限。

新增44项应用检查通过，原164项回归通过。WPF独立项目examples/industrial-station-wpf新增7项离屏/控件状态/关闭检查，共215项。两个项目Release零警告零错误，NG应用demo通过；人工UI/多DPI/长期及实机验收仍未完成。

教程：

- /knowledge/industrial/host-computer/station/operator-window-recipes-and-application-orchestration/
- /knowledge/industrial/host-computer/station/csharp-wpf-station-application-and-recipe-capture/

下一批已有通信任务适配，先模拟/本机回环，之后运动与视觉分批适配。

## 第五批：MC本机回环通信适配

独立项目examples/industrial-station-communication复用原MC邮箱和McpX0.13.0，只将Move接到真实TCP本机回环，其余步骤仍模拟；默认WPF入口不变。新增OpenWithPortAsync(directory,port,clock)显式转移端口所有权，打开失败或正常关闭由应用/宿主释放。

CommunicationStationSession组合应用与CommunicationStationPort，增加远端结果槽释放门槛。工位OperationKey绑定原BootId/RequestId，提交回执丢失保留Ticket；Observe只查询不重发/归零。先保存并消费本地终态，再显式归零/释放远端，最后本地模拟核对。释放回执丢失只读核对原Released证据，不重写Control=2。

新增51项实际TCP回环检查通过，原215工位/WPF回归通过，共266项；旧邮箱55项另行回归通过。新项目Release零警告零错误，丢提交回复demo提交/执行各一次，保存后释放一次并关闭。未连接设备或模型；请求绑定仍在内存，跨进程恢复及真实设备启动代待补。

教程：

- /knowledge/industrial/host-computer/station/communication-task-mapping-and-readonly-reconciliation/
- /knowledge/industrial/host-computer/station/csharp-mc-station-port-and-loopback-session/

下一批运动模块离线适配，再分批接视觉与实机链路。

## 第六批：单轴模型与动作观察适配

独立examples/industrial-station-motion链接旧SimAxis/MotionMath/MotionObservation，把Move接到模型与观察器，其余三步继续模拟。与MC例子是两个独立执行方案，不向同一Move同时发两份动作；默认WPF不变。

显式OpenSimulation/EnableSimulation/BeginReferenceSimulation准备；会话限制在周期外、账本允许时执行。AdvanceSimulation推进虚构时间，同CommandKey观察到运动、反馈/到位及稳定后才完成。工件启动不自动使能/回零，重复操作返回原Task。

StopFlow只锁存流程；RequestOrdinaryStopSimulation明确请求模型减速停止，仍需观察。不合格数据/采样中断/无反馈返回null保留未知，不冒充到位。Task返回null但模型Busy时仍拒绝Dispose，迟到合格结果仅排空不续拍。

Release零警告零错误，新43项集成检查、原266项工位/MC/WPF回归通过，共309项；旧轴68/观察52另行回归。demo显式参考1次、定位1次到反馈2.000mm、模拟NG，保存核对关闭通过。无网络/原生SDK/实际机械，下一批采图与图像处理适配。

教程：

- /knowledge/industrial/host-computer/station/motion-task-reference-feedback-and-stop-boundaries/
- /knowledge/industrial/host-computer/station/csharp-motion-station-port-and-qualified-feedback/

## 第七批：自有帧与视觉处理适配

独立examples/industrial-station-vision引用旧industrial-vision，复用OfflineFrameSource、SingleCaptureSession、InspectionPipeline与Mono8Image。默认WPF/MC/运动入口不变；本入口Move与PublishResult仍模拟，Capture/Inspect真实处理自有合成像素。

回放原8×6 P2图和反相副本，借用按Stride复制后立即归还，再将原请求/源/帧/触发关联到工位FrameId。Inspect只处理原绑定帧，核对原处理器/配方；均值/亮像素阈值得到判定，不读配方名字硬编码Ok/Ng。

SaveResult后AcknowledgeVisionAfterSaved确认原处理结果，最后模拟Review/本地放行。重复Capture/Inspect返回原Task，不重采/重算；错误帧/不合格统计拒绝或形成已结失败。迟到采图不触发Inspect，迟到算法不改Faulted/不发布。

新47项检查、原309工位/通信/运动/WPF回归，共356项通过；旧视觉63/单拍73/流水线71另行通过。Release零警告零错误，demo同配方均值220→35、Ok→Ng、借用归零，正常确认后结果保留0并关闭。

源关联和时间是明确离线声明，未验证真实相机触发与帧龄；帧映射/统计/图片未持久化，文件只保存工位FrameId与终态。没有相机/原生算法/网络，本批不自动组合运动模型。下一批运动与视觉端到端离线组合。

教程：

- /knowledge/industrial/host-computer/station/vision-task-owned-frames-and-inspection-handoff/
- /knowledge/industrial/host-computer/station/csharp-vision-station-port-and-frame-receipts/

## 第八批：运动与视觉离线端到端组合

独立examples/industrial-station-integrated引用现有运动和视觉端口，不复制底层实现。IntegratedStationPort只将Move派发给MotionStationPort；合格后RecordQualifiedExternalMove记录原命令/回复，视觉不再模拟Move。后续Capture/Inspect/PublishResult由原状态机顺序驱动。

IntegratedStationSession显式准备模型与回放源，先保存意图再启动；正常结果保存并消费后确认视觉收据，再模拟核对/放行。StopFlow与模型普通停止分开，迟到Move/帧/判定都不推进原故障周期。关闭等待组合Task，分别释放模块；TryAdvanceSimulation避免关闭泵Tick已关闭模块。

新49组合检查、原356工位/通信/运动/视觉/WPF回归，共405项通过；Release零警告零错误。demo两周期1mm/2mm、帧1/2、均值220/75、Ok/Ng，Move2、视觉模拟Move0、完成证据交接2并关闭。

这是内存运动+自有P2回放的离线主链，不是实际运动触发曝光。结果发布仍本地模拟，没有网络/原生SDK/机械/相机，默认WPF与MC入口不变。图片/FrameStamp/测量明细尚未落盘，下一批补结果留图与追溯。

教程：

- /knowledge/industrial/host-computer/station/integrated-offline-station-motion-to-image-and-result/
- /knowledge/industrial/host-computer/station/csharp-integrated-station-routing-and-single-move-handoff/


## 第十批只读核对

StationLedger.OpenReadOnly(directory)只打开已有账本及已有.writer.lock，不创建目录或文件；复用严格Load/Report，Begin、SaveTerminal、Review拒绝，Dispose释放读锁。扩展说明与控制台核对入口见../industrial-station-evidence/README.md。文件匹配不自动解除启动阻止。
