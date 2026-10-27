# 工业通信：本地 TCP/UDP 教学设备

.NET 10，无第三方包。仅复用上一节纯 C# CRLF 解析器，不依赖 SerialPort 包。
从仓库根目录运行；所有 CLI 网络端点均为 `127.0.0.1`，不是局域网设备。

```powershell
dotnet run --project examples/industrial-networking/IndustrialNetworking.csproj -- --self-test
```

断言使用操作系统真实的本机 TCP/UDP socket 和临时端口，不只是内存替身。
覆盖正常查询、分次写入、关闭/半帧 EOF、错误载荷/请求号、超时、用户取消、
UDP 丢弃模拟、错误来源过滤、旧响应过滤及空/超长数据报。
每个模拟器在检查后停止并等待结束，不保留后台服务。

## 手动运行（每种协议分别开两个终端）

```powershell
# 终端 A
dotnet run --project examples/industrial-networking/IndustrialNetworking.csproj -- --tcp-device 43120 split
# 终端 B
dotnet run --project examples/industrial-networking/IndustrialNetworking.csproj -- --tcp-client 43120
```

```powershell
# 终端 A
dotnet run --project examples/industrial-networking/IndustrialNetworking.csproj -- --udp-device 43121 stalethenvalid
# 终端 B
dotnet run --project examples/industrial-networking/IndustrialNetworking.csproj -- --udp-client 43121
```

客户端打印有效温度 25.3 °C 后释放本次资源；设备端 Ctrl+C 停止。
TCP 43120、UDP 43121 是教学端口，与网站 43110 无关；若被占用，请两端一起换端口。

TCP 场景：`normal`、`split`、`slow`、`close`、`partial`、`malformed`、`wrongid`。
UDP 场景：`normal`、`slow`、`drop`、`malformed`、`wrongid`、`stalethenvalid`、
`wrongsourcethenvalid`、`empty`、`oversize`。

`slow` 等待 2 秒，CLI 客户端响应期限 1 秒；`drop` 是设备主动不回复，并非制造网络层丢包。
`split` 分两次 WriteAsync，但 TCP 可能合并到一次 ReadAsync；不承诺网络读取切分位置。

## 协议和生命周期

请求：`READ <32位小写十六进制请求号> TEMP\r\n`。
响应：`TEMP <同一请求号> 253\r\n`，原始温度单位 0.1 °C，范围 -400～1250。
载荷最多 96 字节，ASCII，CRLF。UDP 一个数据报恰好一帧，不跨数据报拼接。

TCP 每次查询新建连接并释放；连接期限与发送/响应总期限分别设置，超时不重发。
UDP 每次查询新建本地 socket，校验来源 IP/端口和请求号，忽略不匹配数据报但不重置期限。
取消直接传入 .NET 10 网络异步 API 并 await 到完成，不仅是停止等待任务。

`NetworkQueries` 展示客户端，`DeviceSimulator` 展示监听、接受/接收及回复，
`WireProtocol` 管理载荷，`SelfChecks` 提供本机网络故障用例。

这是低频单请求教学服务器，顺序处理连接；慢会话会暂时影响后续请求。
本轮不实现生产级并发服务、TCP 长连接状态机、自动重连、WPF 面板、TLS 或 UDP 身份认证。
IP/端口和请求号校验不构成安全认证。请求号也不自动提供设备端幂等/恰好一次执行。
真实硬件、跨机网络、防火墙、交换机和设备协议兼容性尚需单独验收。
