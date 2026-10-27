# 六轴机械臂基础：纯C#值与上下文检查

.NET 10，无第三方包、网络、SDK、原生库或硬件。以发那科/爱普生课程背景解释概念，不模拟任一厂商控制器，不读取或修改现场。

```powershell
dotnet build examples/industrial-robotics/IndustrialRobotics.csproj -c Release
dotnet run --project examples/industrial-robotics/IndustrialRobotics.csproj -c Release -- --self-test
dotnet run --project examples/industrial-robotics/IndustrialRobotics.csproj -c Release -- --demo
dotnet run --project examples/industrial-robotics/IndustrialRobotics.csproj -c Release -- --task-test
dotnet run --project examples/industrial-robotics/IndustrialRobotics.csproj -c Release -- --task-demo
```

58项离线检查。48组固定的三轴变换往返合计是一项补充数值回归，不是现场验收。Demo输出TCP原点(500,120,30)mm、法兰原点(500,120,-70)mm；都是数学夹具，未计算关节解、可达性或路径。

## 文件和约定

- `FrameTransform.cs`：Vec3、Rotation3、FramedPoint和FrameTransform。右手系、列向量、矩阵数组按行存储，长度mm、角度弧度。TargetFromSource把Source中的点变换到Target；Compose先应用inner。
- `RobotContextReview.cs`：RobotBinding与RobotContextSample，Assess只复核外部声明的普通事实。无历史、许可或派发行为，正向输入默认false。
- `RobotChecks.cs`：58项坐标/普通上下文自检与demo。
- `RobotTaskSession.cs / RobotTaskChecks.cs`：PC侧单任务观察会话与66项任务检查；`Program.cs`只接受上述四个离线参数。

旋转导入复制九个double，正交及det=+1容差1e-9，拒绝缩放/剪切/镜像，不静默修复。Around限定明确轴和±2π弧度，不识别FANUC或Epson姿态字段，不存储关节多圈信息。ApplyPoint检查坐标名；RotateVector没有坐标标签，调用者负责来源。Vec3本身不提供量纲类型保护，有限值或同名坐标都不证明真实参考/标定正确。浮点有限性/运算溢出检查不代替实机行程或机械审核。

RobotBinding的Guid/Revision是外部归一化契约，测试直接构造，不采集设备身份或标定。ToolId/WorkFrameId可为0，含义需对应设备确认。Assess要求同一单调时钟域，默认0.25s只是教学值。多源摘要有效性和时间由外部保守维护；本项目不证明采样一致性，也不检测前后采样缺口。

PrerequisitesObserved=true不表示安全、停止、可达或有权启动。RobotContextReview无状态，新鲜输入可重新通过，不是失联后粘性恢复门。ProgramIdle不证明机械停止。没有机器人连接/读写、运动、使能、复位、暂停/继续、自动重发、持久化、独立看门狗或安全功能。

## 任务会话（独立于无状态普通复核）

66项任务检查和task-demo全部使用脚本化内存报告，不注入真实网络丢包。默认startupReconciled=false阻止写意图；测试直接声明true不证明现场已对账。构造固定一个JobId/配方和RobotBinding，单调用者串行，单保留任务。Prepare冻结请求，TakePublish只取一次本地发布意图；RecordPublish区分未尝试、通信确认和结果未知，不把写回执当业务完成。

RobotMailboxFrame要求外部声明Qualified及有效新鲜Context，检查完整请求、结果形态和版本。Completed+Ok/Ng与Failed分开；Held/Failed、模式/质量/绑定变化、时效缺口或预算过期锁定写入。锁定后仍可输入同执行域、同完整请求的合格晚到终态，只更新历史，不解除锁定或产生ACK/下一任务。HadUncertainty保留未决窗口，终态不因晚来冲突而被覆盖。

正常未中断会话可取一次匹配结果确认意图，再观察空槽释放Current、保存LastReleased；依赖对端只按匹配ACK清槽的外部协议假设，本项目没有实现对端。确认应答丢失例子只构造空槽；不证明真网络一致性。LastReleased仅一条内存历史，不是数据库，外层需实现先可靠记录再确认。结果确认不是复位/停止/产品放行。

时效默认0.25s，总任务及ACK-clear观察预算默认各5s，都是教学值；没有调用方法便没有预算执行。Invalidate只标失效不执行IO。新实例序号/日志为空，不接管另一PC会话任务；无机器人端去重、高水位或跨重启物理exactly-once。发布/确认意图取出不代表任何字节已发送。没有Robot SDK、TP/SPEL+程序、网络编解码、多写者仲裁、启动对账实现、停止或恢复方法。

## 网站教程

- `/knowledge/industrial/host-computer/motion/six-axis-robot-frames-posture-and-modes/`
- `/knowledge/industrial/host-computer/motion/csharp-robot-frame-transforms-and-context/`
- `/knowledge/industrial/host-computer/motion/robot-job-handshake-results-and-reconciliation/`
- `/knowledge/industrial/host-computer/motion/csharp-robot-task-session-and-results/`

资料以官方HTML公开内容为背景；FANUC视频需登录，课程不声称看过登录后内容。数学例子是本项目自定义夹具。未来品牌适配必须先固定本体/控制器、软件及许可、工具/负载、标定/程序与接口语义，本批没有厂商兼容性或现场验收结论。
