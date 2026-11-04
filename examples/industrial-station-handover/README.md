# 交付资料包、现场参数和维护记录

第12步第十二批。net10.0，引用commissioning，无新包、网络、设备SDK或驱动安装。

```powershell
dotnet build examples/industrial-station-handover/IndustrialStation.Handover.csproj -c Release
dotnet run --project examples/industrial-station-handover/IndustrialStation.Handover.csproj -c Release --no-build -- --self-test
dotnet run --project examples/industrial-station-handover/IndustrialStation.Handover.csproj -c Release --no-build -- --demo
```

成功0/异常1/用法2。新建唯一临时目录并保留材料：site-parameters.template.json、maintenance-example.json、acceptance-report.json、handover.md，加handover-manifest.json。它是资料示例，不含工业应用exe、SDK或驱动。工具自身的发布目录是另一份实际可执行.NET发布物。

HandoverPackage.Verify只读固定扁平目录，校验结构/大小/SHA256/缺失/额外/变化/残留，不执行文件。最多6根条目，单份64KiB，禁止链接与任意路径条目；可信专用目录，停止写入后核对，哈希不是签名。CreateNew/Flush/关闭/不覆盖发布，已有目录拒绝，不清理半成品。

SiteParameters只填已知E3032卡族，其余现场参数null，TemplateOnly保持true。Review只做缺项/形状检查，不验证真实设备/端点/单位，也不热更新工位。脉冲卡需另做适配。

MaintenanceRecord含ExampleOnly、记录号/类型、UTC、人员、ReleaseId、配置前后字节哈希及证据/说明；ParameterChange需前后不同。仅数据模型/样例，不是签名审计或完整维护系统。

新增30项检查通过。Demo文件匹配，14项模板提示、8项现场证据缺失。实际win-x64框架依赖文件夹发布、仓库外启动--demo通过；非干净目标机/实际设备验收。默认工位/WPF/MC不变。下一步异常周期原始像素归档。

- /knowledge/industrial/host-computer/station/deployment-handover-site-parameters-and-maintenance/
- /knowledge/industrial/host-computer/station/csharp-handover-manifest-parameters-and-maintenance-records/
