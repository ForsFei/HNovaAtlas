# 工位运动适配：单轴模型与合格观察

.NET10，无第三方包、网络、原生SDK、卡或驱动。链接已有industrial-motion的SimAxis、MotionMath、MotionObservation，引用industrial-station。课程仍以E3032总线卡为硬件方向；此例不是其固件或EtherCAT模拟器。

```powershell
dotnet build examples/industrial-station-motion/IndustrialStation.Motion.csproj -c Release
dotnet run --project examples/industrial-station-motion/IndustrialStation.Motion.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-motion/IndustrialStation.Motion.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2；仅新建系统临时目录保存工位文件，路径打印并保留。没有硬件地址或轴号选项。

## 流程与API

创建MotionStationPort后经MotionStationSession.OpenAsync转移所有权；会话显式OpenSimulation、EnableSimulation、BeginReferenceSimulation，调用方AdvanceSimulation(.01)推进模型与观察。周期内、待核对账本或非Idle状态拒绝准备。底层端口直接使用时仍须遵守单拥有者、周期外准备。

固定demo-ok-v1/1mm、demo-ng-v1/2mm，速度5mm/s；Move与CommandKey绑定，派发前建MotionAttempt，记录调用确认不当完成。观察到运动、匹配指令/反馈/到位且稳定0.1s后形成成功StationReply，其他工位步骤仍模拟。重复同操作返回原Task，不重发。

StopFlowAsync仅锁存工位；port.RequestOrdinaryStopSimulation明确停止模型，需继续Advance观察减速终结。模型已结故障/停止映射失败，采样缺失/总线失效/无反馈/观察超时保持null/Unknown。后来静止不改原Unknown。零位移缺可观察起动默认Unknown，未实现静止/快速命令专用判据。

SaveResultAsync先落盘后消费，SimulateReviewAndReleaseAsync做本地模拟核对，不能代替参考或真实设备条件。CloseStepAsync不自动停止轴，等待任务/待结/终态；Dispose拒绝Busy轴和未结模型Task。

模型时间与宿主TimeProvider分开，推进宿主2s不等于推进轴轨迹。测试等待仅为调度和防挂起，非实时控制。内存Key、绑定和计数不跨进程保存，未连接原生E3032、PLC或视觉。

## 验证

Release零警告零错误，新43项集成检查通过；原266工位/MC/WPF回归通过，共309项。旧单轴68、动作观察52另行回归。覆盖同身份、旧完成位、周期内回零拒绝、稳定/缺反馈、到位信号、停止、故障参考丢失、总线失效、采样缺口、同目标Unknown及关闭迟到排空。

demo：homeCalls1、moveCalls1、feedback2.000mm、模拟Ng，保存/核对/关闭成功。非原生ABI、真实到位精度、时间响应或现场安全验收；默认WPF与MC入口不变。

教程：

- /knowledge/industrial/host-computer/station/motion-task-reference-feedback-and-stop-boundaries/
- /knowledge/industrial/host-computer/station/csharp-motion-station-port-and-qualified-feedback/
