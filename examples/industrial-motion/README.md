# 工业运动教学：纯 C# 模拟与契约检查

.NET 10，无第三方包，不访问网络，不加载雷赛 SDK/原生 DLL，不访问驱动、卡或真实轴。课程硬件方向为 E3032 EtherCAT 总线卡，脉冲卡作为对照；本模型不是其中任一款的固件模拟器。

从仓库根目录运行：

```powershell
dotnet build examples/industrial-motion/IndustrialMotion.csproj -c Release
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --self-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --observation-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --observation-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --manual-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --manual-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --pulse-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --pulse-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --cycle-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --cycle-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --geometry-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --geometry-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --shot-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --shot-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --recovery-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --recovery-demo
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --handoff-test
dotnet run --project examples/industrial-motion/IndustrialMotion.csproj -c Release -- --handoff-demo
```

自检包含 68 项断言。demo 从模拟位置 20 mm 向零点回零，再定位到 25 mm，正常停止后取消模拟使能并关闭。

动作观察另有52项检查：脚本化输入加两项对既有SimAxis的集成观察。`--observation-demo`只输入虚构的回零/定位状态，不发送SDK命令，也不模拟真实驱动回零方式。

手动会话另有43项离线检查，覆盖本地操作条件、持有身份、续期/释放、失焦/模式/互锁、故障源恢复、显式复位与旧事件隔离。三组数量分别为68/52/43，不是实机验收数。

脉冲契约另有53项纯值检查：DMC3000表8.2状态位、未确认/保留位、独立指令/反馈缩放、方向、参考和部分回零配置。无SDK后端、无波形输出，也不模拟卡侧回零轨迹；WPF继续使用原内存轴，不切换为脉冲硬件。

定位工步另有51项纯状态检查：Z退让→XY独立定位汇合→Z接近→指示灯意图→等待阶段的新低/高确认。脚本化注入合格完成报告，不驱动SimAxis、不模拟几何/空间避让/插补、不读写IO；部分失败、未知、失效或超时只阻止后续意图，不停止或回滚设备。

插补几何另有62项纯数值检查：有序二维轴映射、绝对/相对直线解析、绝对圆心与有符号半径圆弧、方向/优劣弧和解析极值包围盒。24组夹具的24024个三角函数点仅作数值补充回归，不算实机测试。没有时间轨迹、SDK派发、设备能力发现、真实组完成或避障，不与工步/WPF自动连动。

单拍证据另有57项纯状态检查：独立输入比较计数、已关联锁存位置和带可靠触发身份的完整帧元数据；三份合格输入才形成关联历史。拒绝计数跳变/溢出、缺身份配图、跨代/过期/冲突与超时，不自动补拍。没有比较器/锁存/相机后端、脉冲、曝光、图像内容或缓冲生命周期验证。

恢复复核另有59项纯状态/值检查：外部九项事实、旧工件处置和当前依据Key，显式复核仍只记录历史。连接/配置/参考/处置/事实变化、质量失效或时效缺口撤销当前确认；不把旧Unknown改为成功，不生成新计划，不驱动已有模型自动恢复/回零/运动/拍照。

PLC—运动交接另有62项纯内存检查：单PLC启动代、单PC执行所有者、单保留任务，身份/载荷去重、一次意图取出、匹配合格结果、终态确认、高水位和失联粘性锁定。只接受位置ID白名单，不装载真实坐标，不调用SDK，不连PLC网络；本地取意图一次不等于跨重启物理exactly-once。

## Windows WPF模拟面板

```powershell
dotnet build examples/industrial-motion/Wpf/IndustrialMotion.Wpf.csproj -c Release
dotnet run --project examples/industrial-motion/Wpf/IndustrialMotion.Wpf.csproj -c Release
```

只操作内存轴，无硬件模式。依次打开、使能、简化回零，鼠标按住±点动，松开/失焦后观察模拟停止；报警输入恢复后仍需显式复位，且不会自动使能/回零。仅支持鼠标，未实现键盘/触摸/手柄点动。

WPF Release编译通过，尚未进行人工窗口操作。UI计时器不是实时线程，卡住后恢复才会检查间隔；租约只在模拟Tick继续推进时生效。关窗采用模拟停止观察和预算，不证明现场机械停止。重建内存轴只重置模型实例（初始1mm），不是硬件复位/清零。

## 文件

- `MotionMath.cs`：count/mm 的 checked Int32 换算、对称半值取整，以及理想零起末速梯形/三角轨迹。
- `SimAxis.cs`：串行使用的单轴、配置、归一化输入、命令身份和快照。
- `SelfChecks.cs`：确定性模拟检查；无物理时间等待。
- `MotionObservation.cs`：本地单动作观察记录、提交证据、停止/完成/失败/未知分类。
- `ObservationChecks.cs`：旧回零位、反馈、采样质量、停止、超时与观察撤销检查。
- `ManualAxisSession.cs / ManualChecks.cs`：串行拥有模拟轴、持有身份、普通停止与显式恢复。
- `PulseContracts.cs / PulseChecks.cs`：版本限定的位域、计数及部分回零配置检查，不是硬件发现或运动许可。
- `PositioningCycle.cs / CycleChecks.cs`：一次性串行工步、跨实例身份、XY结果汇合、输入交接和时效；无真实派发器。
- `InterpolationGeometry.cs / GeometryChecks.cs`：二维mm平面直线/圆弧数值校验与范围；不生成速度或硬件命令。
- `ShotEvidence.cs / ShotChecks.cs`：一次单拍的归一化证据记录，不从比较计数推导电平或从到达顺序推导工件身份。
- `RecoveryReview.cs / RecoveryChecks.cs`：恢复前复核摘要和旧确认失效，不是派发器、安全许可或持久化审计系统。
- `PlcMotionHandoff.cs / HandoffChecks.cs`：PLC请求域到PC本地执行域的内存交接，无运输编码/发布器或真实运动适配。
- `Wpf/`：链接纯模型的简洁WPF窗口，主控制台项目排除此目录的C#文件。
- `Program.cs`：只接受上述十八个离线参数，没有硬件模式。

## 重要限制

`Tick` 推进模拟时间，不是 wall clock、实时线程或 EtherCAT 周期。所有调用由调用者串行执行。回零只模拟从正侧向零点的单阶段搜索；初始位置不在正侧会拒绝，不实现完整驱动器回零方式。

反馈是指令值减注入偏差，不是伺服动力学；无反馈配置返回 null 和 CommandOnly。点动为理想恒速启动，租约只有 Tick 继续推进才会到期，不能代替卡/驱动侧失联看门狗。

模型故障冻结不证明真实机械能即时停止。软件边界在离散采样时检查，无制动余量设计，不是安全功能。关闭不等于停止，Dispose 活动动作结果为 Uncertain；ResetFault 不自动使能、回零或重放。

模型中加减速度为 mm/s²，雷赛 SDK 某些曲线接口接收的是秒；模拟 Jog 方向 -1/+1，不等于 SDK 的 0/1。UnitScale 独立于 SimAxis，不模拟硬件量化。

观察器要求单一命令所有者、同一单调时钟域和归一化输入；本地Key不是设备事务ID。回零策略仅适用于可观察运动及未完成/完成变化的搜索式流程，快速、静止或结果不清零的方法需专用判据。Unknown不证明失败、未执行或停止；Withdraw/CheckDeadline不发送停止，不提供进程卡死后的看门狗。停止证据只到SDK观察层，稳定窗口仅表示若干采样点，不证明机械连续稳定。终态是历史记录，不是实时安全许可。

## 网站教程

- `/knowledge/industrial/host-computer/motion/axis-units-coordinates-and-profiles/`
- `/knowledge/industrial/host-computer/motion/leadshine-e3032-sdk-basics/`
- `/knowledge/industrial/host-computer/motion/csharp-single-axis-simulation/`
- `/knowledge/industrial/host-computer/motion/e3032-drive-homing-and-reference/`
- `/knowledge/industrial/host-computer/motion/csharp-e3032-motion-observation/`
- `/knowledge/industrial/host-computer/motion/e3032-manual-jog-interlocks-and-recovery/`
- `/knowledge/industrial/host-computer/motion/wpf-manual-axis-panel-and-input-lifecycle/`
- `/knowledge/industrial/host-computer/motion/dmc3000-pulse-direction-homing-and-feedback/`
- `/knowledge/industrial/host-computer/motion/csharp-dmc3000-pulse-io-and-position-contracts/`
- `/knowledge/industrial/host-computer/motion/e3032-multiaxis-coordination-and-io/`
- `/knowledge/industrial/host-computer/motion/csharp-e3032-positioning-cycle-and-io/`
- `/knowledge/industrial/host-computer/motion/e3032-line-and-arc-geometry/`
- `/knowledge/industrial/host-computer/motion/csharp-interpolation-geometry-checks/`
- `/knowledge/industrial/host-computer/motion/e3032-position-compare-latch-and-camera-trigger/`
- `/knowledge/industrial/host-computer/motion/csharp-trigger-and-frame-evidence/`
- `/knowledge/industrial/host-computer/motion/e3032-process-pause-stop-and-recovery/`
- `/knowledge/industrial/host-computer/motion/csharp-recovery-review-and-stale-confirmation/`
- `/knowledge/industrial/host-computer/motion/plc-and-leadshine-motion-task-handoff/`
- `/knowledge/industrial/host-computer/motion/csharp-plc-motion-task-handoff/`

脉冲例子中`ConfirmedMask`声明信号解释已确认，不是电平寄存器；SDK状态位不再做原始电平极性反转。两套counts/mm、方向、参考与补偿/采样契约必须独立确认，未知反馈不填零。回零配置支持集合仅为调用者显式提供的夹具，6～9锁存方式不在检查器范围；不自动展开计数器回绕，不自动清零或设置任何硬件参数。

真实 E3032 适配需先固定完整型号、配套 LTDMC.cs/DLL/驱动/固件、伺服与拓扑、轴映射、单位及安全验收条件。本批未执行任何真实运动操作。

工步Pending是未完成意图的只读视图，反复读取不能反复发送；真实单所有者派发器必须按Ticket记录最多一次尝试及未知结果。Report仅接受已经关联并满足外部证据契约的归一化结果，不从原始done推导完成。帧需严格递增，当前接口不支持一个原子样本批量报告两轴完成。中止不自动关输出，完成是历史而非实时许可，换实例不证明旧动作已停止。输入低/高资格无防抖/捕获/工件身份，不当作相机曝光握手；异常恢复不自动重放。

几何策略使用mm、正交平面、输入坐标绝对值≤1e6、最小长度0.001、半径差容差和圆弧包围盒guard各1e-6，均为教学值而非硬件规格。圆心式半径微差原样保留并披露，长度为起点半径参考值，不修复坐标；明显半径不等拒绝。半径式拒绝半圆/近半圆，可用显式圆心表示半圆。整圆、多圈、三点、螺旋、混合旋转轴不在实现内；不推测相对圆心SDK语义。包围盒是数值范围，不是物理避障/限位/停止余量或运动许可。

单拍ContractsConfirmed由外层声明，夹具直接给true；模型不验证真实资源独占、位置单位/方向/参考、单拍关联、缓冲清理或相机触发身份。ArmEpoch每次装载/复位更新，不是设备事件ID，禁止给旧记录重新贴当前代；CameraEpoch独立对应采集/计数重启。主机观察时间不等于曝光时间，不能跨设备时钟直接比较。默认1s总预算/0.25s证据年龄是教学策略；外层驱动期限和失效，进程卡死无独立看门狗。相关/未知均为历史，不重启/重放/停止/关闭输出，不提供多工件连续队列或持久化。

恢复事实由调用者归一化，不采集或证明真实停止、坐标系交接、故障解除、参考、触发/图像对账、工件/输出或资源独占。默认0.25s时效为教学策略，多源摘要须保守维护源时间与各事实有效性；不是原子机械快照或安全看门狗。同值连续新摘要可保留Key，实质变化或时效缺口需重新确认；LastAcknowledgement只保留最后一条历史，不是当前许可。Key是进程内关联而非鉴权/设备事务，确认方法也不证明操作者资质，实际新计划审核/实时条件/派发均未实现。

交接PlcHandoffInput的Valid/RemoteAuto/ProcessPermit默认false，由外层核对后显式输入。接受、一次TakeIntent、RecordDispatch和Qualified报告分别建模；Qualified默认false，不将原始done或SDK返回码直接变成完成。失联、输入代/模式/许可/质量/覆盖丢失后不自动解锁，模型不发停止。新实例有新ExecutorSession但去重历史为空，必须外部启动/旧任务对账后才能运行，不能自动接管PLC保持请求；无持久化、多PC仲裁或独立运动期限。真实配方快照、网络原子性/编码、原生派发及动作观察仍需另行实现。
