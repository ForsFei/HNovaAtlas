# 工业工位质量追溯台

本目录是知识库 Web 全栈系列的同一个演进项目。

## 环境

- Node.js 24，npm。
- .NET 10 SDK。
- Windows 与 Microsoft Edge（当前浏览器验收使用 Playwright 的 msedge 渠道）。
- `lesson-01` 为最初的内存 API；`lesson-02` 为 React 前端；`lesson-03` 为当前 SQLite API。

在仓库根目录安装依赖：

```powershell
npm ci
npm ci --prefix examples/web-quality-trace/lesson-02
npm run verify:trace
```

验收自行生成临时证据和独立数据库，不需要设备、真实工位数据或真实 MES。完整入口顺序执行 API、数据库、契约及普通与实时浏览器检查。失败时看最后一个 `[trace]` 阶段，运行同一命令可复现。

```powershell
npm run verify:trace -- --api
npm run verify:trace -- --browser
npm run verify:trace -- --deployment
```

## 本地运行

先运行基础数据库示例验收，输出中会显示保留的证据与数据库绝对路径：

```powershell
dotnet build examples/web-quality-trace/lesson-03
node examples/web-quality-trace/lesson-03/verify.mjs
```

在一个终端使用输出的数据库：

```powershell
dotnet run --no-build --project examples/web-quality-trace/lesson-03 -- --TraceDb "替换为输出的trace.db绝对路径" --urls http://127.0.0.1:43111
```

在另一个终端：

```powershell
npm run dev:storage --prefix examples/web-quality-trace/lesson-02
```

打开 http://127.0.0.1:43112 。这是教程示例的端口；知识库网站继续使用 43110。

复核模式需设置 Review__Enabled=true，以及 Review__Alice、Review__Bob、Review__Viewer 三个至少 12 字符的本地练习密码，再使用前端 dev:review。实时模式还需 Live__Enabled=true，前端使用 dev:live。不要将真实账号密码写进 Git。

## 管理模式（第 52～56 课）

管理模式启用持久化账号，替代前三个演示密码，并要求所有 `/api` 查询登录（会话入口除外）。先指定一个数据文件，再执行显式初始化：

```powershell
$env:Management__Enabled = 'true'
$env:TraceDb = 'F:\TraceData\trace.db' # 替换为自己的绝对数据路径
dotnet run --project examples/web-quality-trace/lesson-03 -- --Management:Migrate true
$env:Management__User = 'admin'
$env:Management__Password = Read-Host -MaskInput '设置至少12字符的初始管理员密码'
dotnet run --no-build --project examples/web-quality-trace/lesson-03 -- --Management:Bootstrap true
Remove-Item Env:Management__Password
dotnet run --no-build --project examples/web-quality-trace/lesson-03 -- --urls http://127.0.0.1:43111
```

开发时另开终端运行 `npm run dev:management --prefix examples/web-quality-trace/lesson-02`，打开 43112。生产发布默认 Secure Cookie，需通过 HTTPS 入口访问，不适用这个 HTTP 开发启动方式。

管理表版本独立保存在 ManagementSchema；第23课的索引升级仍使用 SQLite user_version。管理升级可安全重跑，未知版本拒绝启动。Bootstrap 只允许账号表为空时运行，不是重置管理员密码的后门。

验收入口：

```powershell
dotnet build examples/web-quality-trace/lesson-03
node examples/web-quality-trace/lesson-03/verify-management.mjs
npm run test:e2e:management --prefix examples/web-quality-trace/lesson-02
```

工位档案是管理资料，停用不会删除历史检测，也不会向 PLC 或设备发送指令。工位编码不可编辑，编辑版本不匹配返回409。账号更新使旧 Cookie 失效；查询员只读、复核员可提交复核、管理员可维护配置与账号。

## 独立发布和恢复

详见 [部署运行簿](deployment/README.md)。发布必须使用新的目录，程序、数据和 Data Protection 密钥分开保管。`--deployment` 会在临时目录发布并执行 HTTPS、备份及升级回退演练，不安装系统服务、不改证书存储、不改真实数据。

## 课程检查点

- 01～17：API、React、SQLite、复核、事件和类型契约。
- 18～30：恢复、健康、指标、索引、游标与证据完整性。
- 31～40：冲突、丢失回执、登录恢复、限流和工作台验收。
- 41～46：后端模块、Hook、组件与导航边界。
- 47～50：CI 配置、日志、条数选择和筛选重置。

文章的路线图为 `content/web/full-stack/overview.mdx`。`lesson-*` 表示代码演进目录，不代表每篇文章拥有一份独立源码快照。

## 当前边界

早期复核账号来自环境变量，是本地练习模型。数据库升级、备份和恢复应先在副本上练习。CI 配置存在不等于远程检查已经通过，必须查看对应提交的 Actions 运行记录。失败日志只上传控制台输出，未收集完整临时数据库。
