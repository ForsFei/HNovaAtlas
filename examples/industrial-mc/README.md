# 三菱 MC / SLMP：C# 字与位读写

.NET 10 / McpX 0.13.0（实际选用net9.0资产），NuGet记录源码提交 `3e883803ec2e6434fd66fafdad2cf938da16ad89`，MIT。
使用真正的McpX客户端与独立教学服务端完成TCP回环，不需要PLC、MX Component或GX工程。

```powershell
dotnet run --project examples/industrial-mc/IndustrialMc.csproj -c Release -- --self-test
dotnet run --project examples/industrial-mc/IndustrialMc.csproj -c Release -- --demo
```

两个入口执行同一有限流程，成功输出 `Completed 46 checks`，退出码0。

## 范围

- TCP / Binary / 3E / Q-L设备指定格式 / 默认直达本站CPU，单连接单所有者顺序访问。
- `McFrames`：有界头解析、响应、字节序和位半字节编码，独立向量核对。
- `LoopbackMcDevice`：只绑定127.0.0.1系统分配端口，小型D/M/X/W数据区；仅0401/1401、最多64设备点。
- `McExamples`：构造连接、using关闭、后台所有权、逐请求间停止、数组数量检查、受约束D110写入。
- `SelfChecks`：46项离线及真实回环检查；`Program`只允许self-test/demo，无外部地址参数。

| 点位 | 类型与初值 | 访问 |
| --- | --- | --- |
| D100 | short=-123，0.1℃ | 只读 |
| D101-D102 | uint=0x12345678，低字先 | 只读 |
| D103-D104 | float=23.5，低字先 | 只读 |
| D105 | ushort=65535 | 只读 |
| D110 | short=250，0.1℃ | 读写0～1000 |
| D120-D127 | 类型/批量演示暂存 | 可写 |
| M100-M104 | true,false,true,false,true | 可读写模拟位 |
| X10 | true；编号十六进制 | 只读 |
| W10 / W16 | 1111 / 2222；编号十六进制 | 只读 |

不提供Y输出、远程RUN/STOP/RESET或密码操作。模拟拒绝统一C059，仅用于测试异常，不模拟各型号错误映射。无扫描、物理IO、安全状态、跨请求原子性或实时性能保证。

## 版本与停止边界

0.13.0构造函数同步连接，`ReadAsync`/`WriteAsync`没有调用方CancellationToken；不是主分支1.x的Options/静态ConnectAsync API。
800ms连接/每请求超时，设备监视定时器为500ms；库TCP实现给单请求累计预算，不代表批量自动拆包或业务全程同一个800ms。
停止在构造/请求之间观察；在途调用await到库完成/超时，不丢任务、不并发Dispose、不伪造即时取消。不配置0无限等待。
超时旧连接不能复用；每轮使用独占对象并Dispose，无自动重试。写入回执丢失即可能结果未知，测试证明已写入但只尝试一次。
已核对bit奇数点数去掉填充、强类型批量字数和返回数量；未声称包校验所有异常载荷/路由。不用于公网不可信服务。
固定包使用BitConverter，示例要求little-endian运行环境。

未测试4E、ASCII、UDP、iQ-R扩展、跨站、随机/大块访问、字符串、真实PLC、长期恢复或WPF界面。

参考：[固定源码](https://github.com/YudaiKitamura/McpX/tree/3e883803ec2e6434fd66fafdad2cf938da16ad89)、[SLMP手册](https://dl.mitsubishielectric.com/dl/fa/document/manual/plc/sh080956eng/sh080956engm.pdf)、[MC手册](https://dl.mitsubishielectric.com/dl/fa/document/manual/plc/sh080008/sh080008ab.pdf)。
