# 实机接入前运行簿与离线演练

第12步第十一批。net10.0，引用原evidence；无新包、网络、设备、驱动安装或原生SDK。

```powershell
dotnet build examples/industrial-station-commissioning/IndustrialStation.Commissioning.csproj -c Release
dotnet run --project examples/industrial-station-commissioning/IndustrialStation.Commissioning.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-commissioning/IndustrialStation.Commissioning.csproj -c Release --no-build -- --demo
```

成功0、异常1、用法2。临时目录打印并保留，不自动清理。默认工位/WPF/MC入口不变。

AcceptanceBook固定八专题，声明基线六部分生成指纹；不是外部文件校验或目标机扫描。每项Simulation/Field分开，缺失保持null，Passed/Failed要求UTC、观察人、证据引用和匹配基线。同内容幂等，不同内容拒绝；重试用新簿。类不认证声明、观察人或证据引用，不生成设备Ready/Start许可。

OfflineDrills三项：原状态机迟到Move回复只排空不Capture；错误Session回复不推进或重复取命令（不是实际相机关联）；原只读对账对缺失路径报不可用且不创建目录。仅这些小范围演练，不代表整个专题通过。Demo三条Simulation，Field全空，MissingFieldEvidence=8。

WriteNewReport写CreateNew .pending、Flush(true)/关闭、同目录不覆盖Move，已有报告/残留拒绝且保留；64KiB上限。无跨目录事务、签名、断电或现场质量/长期验证保证。

新增34项记录/基线/演练检查。下一批部署交付包、现场参数模板与运行维护记录；实际E3032/相机/PLC待按现场版本与条件验证。

- /knowledge/industrial/host-computer/station/field-commissioning-baselines-and-fault-rehearsals/
- /knowledge/industrial/host-computer/station/csharp-acceptance-book-and-offline-fault-drills/
