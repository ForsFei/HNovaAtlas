# WPF 模拟读数面板：验收与发布检查表

此清单只适用于 `examples/wpf-reading-dashboard/` 的教学示例。它不连接真实设备，也没有安全联锁；通过这些检查不代表可用于生产控制。

## 构建与可重复断言

- [ ] `dotnet build examples/wpf-reading-dashboard/WpfReadingDashboard.csproj -c Release` 无警告和错误。
- [ ] `dotnet run --project examples/wpf-reading-dashboard-checks/WpfReadingDashboardChecks.csproj` 全部断言通过。
- [ ] 记录 SDK 版本、Windows 版本、目标架构与提交版本；测试新旧版本时使用同一组输入。

## Windows 桌面人工验收

- [ ] 读取成功显示 24 条；读数、报警数和列表状态一致，读取期间窗口仍可响应。
- [ ] 连续快速点击不启动并行读取；取消或模拟故障后旧批次仍可见，可再次读取。
- [ ] 阈值无效值不可应用；有效值更新行状态与“仅显示报警”视图；撤销只影响草稿。
- [ ] 在读取中关闭，确认没有迟到 UI 更新、未观察异常或进程滞留。
- [ ] 在目标缩放、主题、键盘操作和屏幕阅读器下检查布局、焦点、名称和对比度。

## 发布与运维决定

- [ ] 选定框架依赖或自包含。框架依赖时核对目标机 .NET Desktop Runtime；自包含时核对包大小和安全补丁维护方式。
- [ ] 以目标 RID（如 `win-x64`）发布 Release，完整复制并校验整个 `publish` 文件夹；在干净目标机启动，而非只在开发机运行。
- [ ] 若正式分发，另行决定安装/卸载、代码签名、更新和回滚机制；`dotnet publish` 本身不提供这些完整流程。
- [ ] 真实设备接入之前，单独设计协议、超时、重试幂等性、权限、日志、告警确认和安全联锁，并做故障注入与现场验收。

参考：[.NET 发布概览](https://learn.microsoft.com/en-us/dotnet/articles/core/deploying/index)、[WPF 线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)。
