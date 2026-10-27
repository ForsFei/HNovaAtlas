# Modbus RTU/TCP 基础与 NModbus

固定复现基线：.NET 10、NModbus 3.0.83、NModbus.Serial 3.0.83、System.IO.Ports 10.0.0。
版本以已还原包与其源码 commit `d96e7707a08cebe7f436087e7d575c930963d91a` 为依据，
不表示未来最新版本。请保留 NModbus 及各依赖的许可证信息。

从仓库根目录运行：

```powershell
dotnet run --project examples/industrial-modbus/IndustrialModbus.csproj -- --self-test
dotnet run --project examples/industrial-modbus/IndustrialModbus.csproj -- --demo
```

不带参数也运行 demo。内部设备仅监听 `127.0.0.1` 系统分配端口，无局域网扫描/广播。
所有写入只改变这个进程内的模拟存储，退出后不保留；客户端和服务端均在退出时释放。
模拟器是独立编写的有限功能服务端，NModbus 负责客户端，不是拿同一库两端互测。

## 教学数据表（Unit ID = 1，所有 API 地址从 0 开始）

| 区域 | 偏移 | 初值 | 含义 |
| --- | --- | --- | --- |
| Coil | 0 | false | 模拟 Enable 灯，不控制真实输出 |
| Discrete Input | 0 | true | 模拟 Ready |
| Input Register | 0 | 0xFF85 | Int16 -123，×0.1 = -12.3 °C |
| Input Register | 1～2 | 0x41C8, 0x0000 | 约定高字在前 Float32 = 25.0 |
| Holding Register | 0 | 250 | 模拟目标值，×0.1 = 25.0 |
| Holding Register | 1 | 1 | 模拟模式值 |
| Holding Register | 2 | 0 | 模拟计数 |

四个区域各有 8 个点，偏移 0～7；超出区域返回异常码 02。demo 会修改 Coil0、Coil2～4、
HR0～2 并读回。写入回执和读回一致都不代表运动执行完成。

## 文件

- `ModbusExamples.cs`：工厂/主站对象、超时与重试配置、TCP 有限工作单元；RTU 方法只编译，不由 CLI 调用。
- `RegisterCodec.cs`：有符号缩放、高字在前 UInt32/Float32；其他排列必须按设备手册另行约定。
- `ModbusFrames.cs`：离线 MBAP 和 CRC 检查；实际客户端报文由 NModbus 构造，不手动追加 CRC。
- `LoopbackModbusDevice.cs`：FC01/02/03/04/05/06/0F/10，异常和故障注入；不是完整生产 Modbus 服务器。
- `SelfChecks.cs`：实际回环读写、异常码、事务号、超时/取消与离线 RTU 配置。

## 超时与取消边界

该版本常见 Async 方法用 Task.Run 包装同步调用，没有 CancellationToken 重载。
本例由一个工作任务独占 master，`ReadTimeout/WriteTimeout=400`，`Retries=0`，
`SlaveBusyUsesRetryCount=true`；禁止自动重发普通失败与 Busy 请求。
ACK 05 仍可能让库继续读响应；单次读取超时也不是整个事务或整批工作的期限。

TCP 包装器用一次总预算覆盖建连与整个委托，在取消/预算到达时关闭底层 TcpClient 以结束 I/O，
再 await 同步工作任务退出；master 仍由工作任务自己释放。不是只 WaitAsync 后返回留下后台读取。
关闭连接不回滚已经送达的写命令，写入失败/超时的现场执行结果可能未知。
这个方案不能强行终止任意不检查令牌的 CPU 代码，也不能当作 COM 驱动取消方案。
调用者不应把同一个 master 分给其他线程/定时器，不要额外嵌套无限重试。

故障检查包含 Busy、ACK、无响应、关闭、错误事务号及慢速字节；均为本机模拟。
RTU 只构造未打开的 SerialPort/主站并检查配置；没有打开 COM，没有验证 RS-485 方向、
静默间隔、电气接线、CRC 现场差错或设备响应。`ReadRtuTemperature` 需要未来显式选择的测试设备环境。

本目录保持命令行入门；后续数据解释/轮询与受约束 WPF 写入位于 `examples/industrial-modbus-monitor/`。
