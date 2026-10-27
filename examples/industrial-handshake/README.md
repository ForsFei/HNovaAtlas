# C# 单命令邮箱与结果查询

.NET 10 / McpX 0.13.0，复用相邻`industrial-mc/McFrames.cs`。真实MC TCP回环，独立模拟扫描；无PLC/驱动/硬件操作。

```powershell
dotnet run --project examples/industrial-handshake/IndustrialHandshake.csproj -c Release -- --self-test
dotnet run --project examples/industrial-handshake/IndustrialHandshake.csproj -c Release -- --demo
```

两入口运行同一有限流程，成功为 `Completed 55 checks`、退出码0。

## 文件

- Contract：不可变Ticket、状态Snapshot、回复Reply、证据Resolution；显式16位字编码与状态语义校验。
- Mailbox：单发布者、单保留请求、六字候选参数、Control/Ack四阶段、重复/冲突/高水位与结果释放。
- LoopbackController：仅127.0.0.1随机端口，MC字读写子集；独立50ms模拟进度，无物理动作。
- HandshakeClient：同步构造放后台，逐调用using关闭；提交/释放、状态查询与业务等待。
- SelfChecks：55项纯逻辑及真实网络正常/故障检查。

## 字段

| 地址 | 字段 |
| --- | --- |
| D200-D201 | ExpectedBootId uint，低字先 |
| D202 / D203 / D204 / D205 | RequestId ushort / Operation ushort / Target short / Version ushort |
| D206 | Control：0归零通道，1提交，2释放终态 |
| D240 | RevisionBegin |
| D241-D242 | BootId |
| D243-D244 | HighWater / CurrentId |
| D245-D248 | State / Result / AcceptedId / CompletedId |
| D249-D251 | 已锁存Target / Operation / Version |
| D252-D255 | Control镜像 / ControlAck / ReplyId / Reply |
| D256-D257 | ExecutionCount测试诊断 / RevisionEnd |

候选参数仅在Control/Ack=0时可写，完整写完后提交。设备锁存后不依赖后续候选区变化；状态快照锁复制并校验头尾版本。Recorded只表示记录候选，不能代替Accepted或Done。
相同(BootId,RequestId)+相同载荷返回Duplicate，不重复开始；同身份改参数Conflict。终态保持到显式Release，HighWater继续拒绝旧编号。新编号不能覆盖当前未释放结果。
这是自定教学协议，不是MC标准的设备功能。

## 恢复与边界

- 提交可能发出后异常为Unknown，保留原Ticket。Query/Wait只读，不重放Control=1。
- 若提交回执丢失，查询能找到同一保留结果；核对后可显式`ResetObservedControlAsync`写0归零。这个方法不是只读，但不提交/释放动作。
- 控制通道复位、结果释放、CPU复位、故障清除、设备停止完全不同；本例只实现前两项，没有CPU复位、故障清除或设备停止能力。
- 本地等待超时/取消不撤回动作。0.13.0无读写调用方token，等在途I/O完成/800ms请求期限；预算非硬实时保证。
- `Known`不是动作成功。Done还要关联同一Ticket；Rejected/Faulted也是已知结果。NotRetained不能推出没执行。
- BootId模拟从1开始，只在同一对象Reboot递增；无可靠跨进程启动身份。RequestId和发布Revision不回绕，耗尽需受控处理。
- 单调用者顺序操作，无多PC仲裁、持久化/掉电日志、跨设备事务、安全认证或物理动作验证。ExecutionCount只供测试，不是持久化去重凭据。
- 网络侧锁复制快照不证明现场PLC多字原子性。实际程序需要验证扫描锁存、写入顺序、单字原子性、发布窗口和互锁。

故障覆盖：部分参数、保持电平、重复/改参冲突、Busy、提前/错误释放、互锁、执行故障、坏版本/完成编号、提交/释放回执丢失、重启、超时取消与关闭。网络/扫描任务退出均await，命令行不接受硬件地址。
