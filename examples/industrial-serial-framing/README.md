# 串口组帧与 WPF 交接

固定复现基线：.NET 10、System.IO.Ports 10.0.0；WPF 仅 Windows。
从仓库根目录运行：

```powershell
dotnet run --project examples/industrial-serial-framing/Checks/SerialFraming.Checks.csproj
dotnet run --project examples/industrial-serial-framing/Wpf/SerialFraming.Wpf.csproj
```

默认不勾选 COM：连接 → 读取一次温度 → 显示 25.3 °C → 断开。
内存设备故意把响应拆为 `TE`、`MP=2`、`53\r`、`\n`，它不是虚拟串口驱动。

真实/虚拟串口测试需要自己准备已连通的端口对：在另一个终端运行前一节的设备端，
再在 WPF 中勾选 COM、填写对端端口与波特率后连接。例如明确配置了 COM3 ↔ COM4 时：

```powershell
dotnet run --project examples/industrial-serialport/IndustrialSerialPort.csproj -- --device COM4 9600
```

然后 WPF 选择 COM3、9600。不要把示例端口名当作当前机器的可用设备。
串口采用 8N1、无握手、ASCII、CRLF。请求 `READ TEMP\r\n`，响应 `TEMP=253\r\n`。
仅适用于本教学协议，不要对未知工业设备发送这些字节。

## 代码入口

- `Core/CrlfFrameDecoder.cs`：有界、单消费者、可打印 ASCII、CRLF 分帧。
- `Core/SerialSession.cs`：后台单所有者，最多一个排队/在途请求，2 秒响应期限；异常向上抛。
- `Core/BytePorts.cs`：真实 SerialPort 适配与内存设备；100 ms 同步读取等待、500 ms 写入等待。
- `Wpf/MainWindow.xaml.cs`：Dispatcher 交接、异步关闭、历史最多 100 条。
- `Checks/Program.cs`：分段、连续多帧、异常数据、停止与失败后的释放检查。

取消令牌不直接中断 SerialPort 同步读；后台在有限读取等待后检查停止。
2 秒是应用响应期限，不是 Windows 驱动退出时间保证。写入/打开/释放仍可能受驱动影响。
异常、响应超时、未请求的字节和多余响应都结束会话；不自动重发。
协议没有响应 ID，关闭重开也不保证物理设备不会继续发送旧响应；实际接入必须约定同步规则。

离线检查不打开 COM，不能验证真实驱动、RS-485 方向切换、电气连接或设备行为。
页面教程：`/knowledge/industrial/host-computer/communication/serial-receive-framing-and-wpf/`。
