# OPC UA 本机安全读写与订阅

.NET 10；OPC Foundation Client/Server 2.0.1；Microsoft.Extensions.Hosting 10.0.12。
源码基线：包 nuspec 指向 UA-.NETStandard 提交 `07e62f72500cc0923a2bab940653c8c1116fa286`。

从仓库根目录运行：

```powershell
dotnet run --project examples/industrial-opcua/IndustrialOpcUa.csproj -c Release -- --self-test
dotnet run --project examples/industrial-opcua/IndustrialOpcUa.csproj -c Release -- --demo
```

两个参数执行同一有限完整流程，不提供外部地址或设备扫描，不安装系统服务。

## 文件与主线

- `DemoHosts.cs`：客户端/服务端 DI 配置、真实服务就绪信号、受限本机证书批准。
- `DemoModel.cs`：明确字符串 NodeId，三个模拟变量；服务端检查 Setpoint 有限 Double、0～100。
- `UaOperations.cs`：URI 解析、Good/类型检查、单次 WriteAsync。
- `Program.cs`：29项检查；证书负例后，双向信任再安全连接、浏览读写、订阅、取消关闭。

三个变量的 namespace URI 为 `urn:hnova:industrial:opcua:demo`：

| 标识符 | UA类型 | 初值 | 权限 |
| --- | --- | --- | --- |
| Demo.Temperature | Double | 23.5 | 只读 |
| Demo.Ready | Boolean | true | 只读 |
| Demo.Setpoint | Double | 25.0 | 读写，0～100 |

每次运行新建 `.local/<GUID>/server` 和 `client`；包含测试私钥，已忽略。仅批准本次自己生成的公开证书，打印 SHA256，不批准 rejected 目录，不碰 Windows 证书库。
SDK 默认生成多算法应用证书；双向 AutoAcceptUntrustedCertificates=false，CheckDomain=true，仅选择 Basic256Sha256 / SignAndEncrypt，无 None 降级。
前两个阶段 SDK 的证书拒绝 error 日志是预期负例；结尾必须为 `Completed 29 checks`、退出码0才算通过。

服务只绑定127.0.0.1临时端口，程序实际检查监听地址。先保留再交接端口仍存在竞争，冲突直接失败。启动要等待 UA startup task，不能只等待 Generic Host.StartAsync。总工作预算三分钟，取消为协作机制，不保证任意驱动实时退出；退出时 await 资源清理。
程序完全退出后，可按明确路径手工清理自己的 `.local/<GUID>`，不要上传其中私钥。默认保留以便检查证书存储，不会删除历史目录。

这是隔离教学设备，匿名身份仅供本机练习；不是现场证书审批方式，也不模拟物理过程。写入 Good/读回一致不表示机械动作完成，不自动重发写入。
尚未验证现场 PLC、账号授权、CA/CRL、证书续期、断线恢复/重订阅、历史、报警或WPF界面。

依赖来源与许可见 [OPC Foundation仓库](https://github.com/OPCFoundation/UA-.NETStandard/tree/07e62f72500cc0923a2bab940653c8c1116fa286) 和各 NuGet 包附带 LICENSE；运行无原生 Snap7 依赖。
