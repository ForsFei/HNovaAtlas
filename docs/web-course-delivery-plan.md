# Web 全栈后续交付记录

用户授权：连续完成阶段收尾、工位管理、持久化用户权限、统计看板、部署恢复，完成后统一汇报。

## 计划

1. 收尾：运行说明、完整验收、独立分支推送、GitHub Actions 与失败日志验证。
2. 工位管理：创建、编辑、停用、字段校验、唯一编码、乐观并发及可操作页面。
3. 用户权限：持久化账号、密码哈希、三角色、停用与会话撤销；保留旧课程默认行为。
4. 统计：按 UTC 范围/工位汇总检测、NG、待复核，与明细一致并提供可读展示。
5. 部署：程序与数据分离、配置、HTTPS 反向代理、备份、升级、回滚演练。

## 当前进展

- 第 51 课与运行说明已交付；前 50 课完整验收 9 阶段通过（293.7 秒）。
- 独立分支 codex/web-course-delivery，基线提交 9202ee60facb80105cf40517ec95e9f6dc3185b1，已推送。
- Git 使用单次命令代理 http://127.0.0.1:7890，未修改全局代理。GitHub 连接器读操作可用，创建 PR 返回 403；必要 REST 只在内存使用已缓存 Git 凭据，不输出凭据。
- 首次远程运行 https://github.com/ForsFei/HNovaAtlas/actions/runs/43138151848：内容与 API 通过，契约检查因 Windows CRLF 误报失败；日志 artifact 13908896833 确实上传成功，保留七天。
- 已修复生成类型对比的 CRLF/LF 差异，新增内存注入回归，真实必需字段漂移检查仍失败如预期；修复已随9982f22推送并在远程通过。
- 已增加 ManagementDb/ManagementEndpoints，以 Management:Enabled 显式开启；Management:Migrate 创建版本 1 附加表，Management:Bootstrap 仅空账号表创建首个管理员。共享追溯 SQLite，保留旧课程默认行为。
- 工位 CRUD（停用替代删除）、账号哈希/角色/版本会话撤销、审计、按导入 UTC 时间统计已实现；verify-management.mjs 首轮全部通过。
- ManagementConsole 管理模式与独立浏览器验收已通过：创建/双人冲突/草稿/停用、账号持久化、权限界面、统计、停用后会话撤销。
- 管理 API 已补 OpenAPI名称、响应模型、PUT-CSRF与授权元数据；UTC包含/排除边界与未知工位统计通过。
- 第52～56课、完整路线和部署运行簿已完成。管理模式追溯选择器使用档案与历史工位编码的合集，统计支持同范围明细。
- 发布、独立数据目录、SHA清单、Secure会话/CSRF、受信loopback代理协议头、在线快照、未知结构拒绝启动、回退与统一会话撤销均已实现并实际演练。
- 最终统一验收13阶段（API、三种浏览器模式、契约换行回归、发布恢复）全部通过，1138.2秒。
- 功能提交9982f227091ac4bc18cc67ef1644a2c50bfbbaf1的远程CI全部通过：https://github.com/ForsFei/HNovaAtlas/actions/runs/43144499734 。包含部署与恢复演练。
- 知识库49项测试、根TypeScript和新增文章编译已通过。额外全站构建发现第13课表格路由的{id}误作MDX表达式，已改为行内代码，并新增57篇Web教程实际渲染检查，全部通过；CI也纳入该检查。
- 交付入口：examples/web-quality-trace/README.md；部署运行簿：examples/web-quality-trace/deployment/README.md；完整学习路线：content/web/full-stack/overview.mdx。
- 最新提交的远程记录以本分支Actions为准：https://github.com/ForsFei/HNovaAtlas/actions/workflows/web-quality-trace.yml 。真实公网域名、目标机服务安装和现场长期运行不在本机演练结果内。

## 文件操作

遵循用户 TSD 约定。C# 使用已验证 Python 写入同目录新临时文件后原子替换，并扫描物理头。不得通过 Git 检出覆盖 C#。
