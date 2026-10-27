# E3032 只读诊断：可测试的 C# 采样层

.NET 10，无第三方包、网络、P/Invoke、原生 DLL 或硬件模式。默认和唯一后端为 `FakeReadOnlyDmc`，不是雷赛固件、驱动或 EtherCAT 模拟器。

```powershell
dotnet build examples/industrial-leadshine-diagnostics/IndustrialLeadshineDiagnostics.csproj -c Release
dotnet run --project examples/industrial-leadshine-diagnostics/IndustrialLeadshineDiagnostics.csproj -c Release -- --self-test
dotnet run --project examples/industrial-leadshine-diagnostics/IndustrialLeadshineDiagnostics.csproj -c Release -- --demo
```

81 项断言覆盖应用层配置、初始化计数/卡身份、控制器模式、总线诊断、从站数、轴类型/映射/当量、错误码、数据时效、取消、超预算、故障可观察性和资源失败。

## 结构

- `ReadOnlyDmc.cs`：应用接口、结果、固定配置、不可变快照，不是原生 ABI 声明。
- `CardProbe.cs`：初始化/关闭的单所有者，单次只读采样及新鲜度判断。
- `FakeReadOnlyDmc.cs`：可编程测试替身和单调测试时钟。
- `SelfChecks.cs`：离线断言。
- `Program.cs`：固定替身的 CLI，只有 `--self-test`/`--demo`。

单调用者串行拥有同一 SDK 资源域；本例没有实现进程内多所有者仲裁、跨进程锁或后台轮询框架。接口不包含复位、清错、使能、运动、配置下载或写 IO。

## 配置只是虚构夹具

卡号3、固件类型0x1234、轴0、节点1001/子轴0、从站2、当量4000均是测试数据；尤其0x1234不是E3032产品类型编码。真实配置必须从用户版本 SDK 和已验证工程取得，不可直接照抄。

## 验证边界

`IReadOnlyDmc` 尚无用户配套 `LTDMC.cs`/DLL 后端。官方接口映射见网站教程，原生互操作、数组容量、位数、线程约束及实际返回值仍需逐项编译和验收，不通过反射或猜测 P/Invoke 绕开。

顺序读取不是原子快照：前后核对映射和总线只能检测部分变化，不能识别所有瞬变或同型号设备替换。类型/节点/数量匹配不证明机械方向、轴身份、参考有效或安全条件成立。

`IsCurrent` 从最早读数起按单调时钟计算年龄，失败/取消/关闭使 Latest 失效。读到报警仍可生成有效诊断，数据有效不等于允许运动。没有真实反馈、回零和安全输入数据，不据此判定到位或运动许可。

取消仅发生在同步调用前后；超预算仅在调用返回后拒绝发布，不能中断卡住的原生调用。不用 Task.WhenAny 丢下后台 I/O。关闭失败会阻止同一实例再初始化，并不自动重试全局 close，也不证明机械停止。

尽管接口称为只读诊断，真实后端的资源初始化/关闭仍需现场明确许可、独占和设备状态评估；本例没有执行这些真实调用。

教程：

- `/knowledge/industrial/host-computer/motion/e3032-ethercat-commissioning-and-axis-mapping/`
- `/knowledge/industrial/host-computer/motion/csharp-e3032-readonly-diagnostics/`
