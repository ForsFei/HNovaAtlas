# PLC 通信首批：S7.NET+ 与本机 Snap7

复现基线：.NET 10、S7netplus 0.20.0（源码提交 f1ae0ea084e712b59e414de6aaee7d196244a239）。
版本固定，不表示以后最新版本。实际设备的 CPU/固件、通信权限和 DB 布局必须另行确认。

## 运行

纯离线检查不加载 Snap7，也不建立网络连接：

```powershell
dotnet run --project examples/industrial-s7/IndustrialS7.csproj -- --offline-check
```

Windows x64 上按本机脚本策略运行一次依赖准备，再运行网络检查或 demo：

```powershell
./examples/industrial-s7/prepare-native.ps1
dotnet run --project examples/industrial-s7/IndustrialS7.csproj -- --self-test
dotnet run --project examples/industrial-s7/IndustrialS7.csproj -- --demo
```

准备脚本从 S7.NET+ 固定提交的测试目录下载 Windows x64 Snap7，校验固定 SHA256，
并保存 GPLv3/LGPLv3 许可文本到忽略目录 `.local/`。程序加载前再次校验哈希。
这是复现/完整性检查，不是安全认证；请阅读 THIRD-PARTY-NOTICES.md。
DLL 不提交到仓库，默认构建只在它存在时复制到输出目录；准备完成后必须重新构建再运行。

demo 内置服务仅绑定 127.0.0.1，使用一个临时端口，不访问 PLC、不扫描网段、不安装 Windows 服务。
原生库要求一个明确端口，程序先临时申请再释放端口，然后让 Snap7 绑定；若端口恰好被占用则报错，
不扫描其他端口、不停止任何已有服务。所有客户端关闭后停止/销毁测试服务，最后释放固定内存与模块。

## 固定 DB1 数据契约

| 地址 | 类型 | 初值/说明 |
| --- | --- | --- |
| DB1.DBX0.0 | BOOL | Ready=true |
| DB1.DBB1 | BYTE | Mode=2 |
| DB1.DBW2 | INT | -123，×0.1 得 -12.3 |
| DB1.DBD4 | DINT | 123456 |
| DB1.DBD8 | REAL | 25.5 |
| DB1.DBW12 | INT | 模拟目标250，demo写300 |
| DB1.DBX14.0 | BOOL | 模拟请求标志；不是现场动作命令 |
| DB1.DBX14.1 | BOOL | 无关标志，初值true，不能被覆盖 |
| DB1.DBB16起 | STRING[8] | 最大/当前长度头 + ATLAS01 |

读写只影响64字节测试DB。没有注册输出Q区，也不执行CPU启动/停止、下载或真实运动。
CpuType.S71200、rack0/slot1只是本机演示连接配置，不宣称模拟了S7-1200固件或安全设置。

## API 与风险

`Plc.OpenAsync` 完成 TCP/COTP/S7 建立；`ReadBytesAsync` 返回原始字节，`ReadAsync` 返回 object?，
`ReadMultipleVarsAsync` 使用 DataItem 描述类型和偏移，`WriteAsync` 按实际传入值序列化。
DBW 字符串读取是 Word，不自动解释为 INT；DBD 也不自动解释为 REAL。
明确使用 VarType.Int/Real 或字节转换，并检查返回值类型。

写入 DBW12 时传 `(short)300`；未显式标型的 `300` 是 Int32，不能因为地址写了 DBW 就假定它只写两字节。
本示例只允许回环目标和0～1000原始目标值，写完读回，位写入要保留同字节的其他标志。
位写入也不构成业务互锁，不能代替PLC侧动作握手。

一条 Plc 对象由一条异步流程拥有，所有操作顺序 await，使用一次总预算。
本例在.NET 10选择包内net7.0资产，ConnectAsync和所用读写传令牌；旧目标框架不应直接沿用此结论。
ReadTimeout/WriteTimeout不等于整批异步总期限。失败/取消后关闭旧对象，不自动重发写入。
固定版本有些协议错误返回普通Exception，并非全部PlcException；应用边界应记录实际错误，
不要照抄过期文档中的LastErrorCode/LastErrorString接口。

## 验收边界

离线检查验证配置、地址解析、DB/字符串/数值解释和写入类型长度。
回环检查验证协议建立、原始/类型/多变量读、目标/位写入、无关位保留、非法DB及取消/关闭。
额外用只接受TCP但不回握手的本机端点验证取消，所有操作均await到退出。

Snap7不是TIA PLCSIM、PLC扫描周期、现场I/O、权限/证书或安全系统的替身。
本轮不改工程、不开PUT/GET权限、不禁用DB优化，也不声称实机验收。
MC与OPC UA仅在路线概览中比较，尚未实现它们的客户端/证书/订阅。
