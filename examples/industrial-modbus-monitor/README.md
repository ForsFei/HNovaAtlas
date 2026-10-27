# Modbus 数据解释、轮询与受约束写入

固定 .NET 10 / NModbus 3.0.83 / NModbus.Serial 3.0.83 / System.IO.Ports 10.0.0。
链接复用上一节客户端和回环设备，不复制一套漂移的 Modbus 传输实现。

从仓库根目录运行：

```powershell
dotnet run --project examples/industrial-modbus-monitor/Checks/ModbusMonitor.Checks.csproj
dotnet run --project examples/industrial-modbus-monitor/Wpf/ModbusMonitor.Wpf.csproj
```

WPF 仅 Windows。选择 None 后启动，等待首次有效采样，再输入 `30.0`，点击“写入一次”。
程序只操作自己的 `127.0.0.1` 临时端口；不能输入任意 IP、Unit ID、地址或功能码。
每轮建立并释放一条连接，所有同步 master 调用由一个后台任务顺序执行；不是长连接池。
适合低频教学；高频或设备限制连接数时需改成明确所有者的长连接。

## 数据与界面

- IR0：Int16、×0.1；IR1～2：Float32 ABCD；只接受有限浮点值。
- HR0：目标，UInt16、×0.1；只允许人工输入 0～100，粒度 0.1，不隐式四舍五入。
- HR1/2：模式/计数；DI0：Ready。不同功能区顺序读取，不承诺原子工位快照。
- Core 发布不可变快照；UI 每 200 ms 读取，采样超过 3 秒无效，历史最多 100 条。
- ASCII/位字段和四种 32 bit 排列由独立 Codec/检查展示；UI 不增加无必要的排列选择器。

## 写入状态

最多一个排队或在途命令，默认排队有效期 2 秒；没有新鲜读数、停止或故障时拒绝新命令，发送前再次检查采样新鲜度。
只写 Unit1/HR0，不提供任意地址写入。每个命令只尝试一次：

- Queued：尚未发送；停止/过期可变为 NotSent。
- Sending：即将调用写方法；不能声称此时停止就能撤回。
- Verified：写回执正常，随后读回一致，不代表物理动作完成。
- Mismatch：写回执正常，但读回不同。
- Rejected：本次写调用收到明确协议异常；记录异常码。
- Uncertain：写入已尝试，但没有得到确定回执；不自动重发。
- Acknowledged：收到写回执，但没完成读回验证。

默认轮询间隔 1 秒（整轮完成后延时），每轮总预算 1.5 秒；连续三轮读取失败停止。
读取可以在失败后有限恢复，写入不重新入队。已排队命令在读取故障/停止时取消；过期不执行。
写入结果保留在停止后的快照中，不能因停止而改写成“未发送”。

## 模拟故障

WPF 可选 `RejectWrites`（写入返回03）、`AdjustSetpoint`（模拟设备调整目标，读回不一致）、
`ApplyWriteThenDropOnce`（先改模拟值，后丢回执并断开）。最后一种场景恢复读取后可能显示新目标，
但旧写入仍显示 Uncertain，程序不会自动重写或根据稍后读数倒推旧命令已验证。
检查另用 `ApplyWriteThenSilence` 验证“已尝试写入时取消”与普通静默读取失败。

## 验证边界

Checks 覆盖四排列浮点黄金向量、Int32/UInt32、NaN/Infinity、ASCII 填充、标志位、
目标输入边界、排队互斥/过期、正常写入/拒绝/读回不一致/回执丢失，以及停止清理。
实际使用本机 TCP + NModbus，但不操作真实设备或 COM。
Core 检查和 WPF 编译不等于已人工操作窗口或验收现场交互。
这不是生产安全功能、实时控制或完整设备驱动；现场写权限、互锁、动作握手和业务幂等需另做。
