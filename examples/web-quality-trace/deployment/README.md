# 发布与运行簿

要求 Windows、PowerShell 7、.NET 10 运行时；发布机还需要 SDK、Node24、两份 npm 依赖。代码变更后先运行根目录 `npm run verify:trace`。

## 1. 发布到新目录

```powershell
pwsh -File examples/web-quality-trace/deployment/Publish-Trace.ps1 -Destination F:\TraceReleases\v1 -DataDirectory F:\TraceData
```

已存在的发布目录会被拒绝。程序目录与数据目录不能互相包含。失败发布留下 RELEASE-INCOMPLETE，不应运行；核查原因后选新目录发布。

输出包含 .NET 框架依赖发布、React 静态文件、生产配置、运行脚本和 SHA256 清单。清单检测意外变更，不是数字签名。配置已进入清单，修改配置后必须重新发布，不要随意更新清单掩盖差异。

本机若有透明加密，发布过程检查文本物理头；源文件明文不代表复制后的脚本也明文。运行目录与源目录都需复查。

## 2. 初始化与引导管理员

```powershell
pwsh -File F:\TraceReleases\v1\Run-Trace.ps1 -Mode Migrate
$env:Management__User = 'admin'
$env:Management__Password = Read-Host -MaskInput '初始管理员密码（12～256字符）'
pwsh -File F:\TraceReleases\v1\Run-Trace.ps1 -Mode Bootstrap
Remove-Item Env:Management__Password
pwsh -File F:\TraceReleases\v1\Run-Trace.ps1 -Mode Serve -Port 43111
```

密码不要写入命令参数、配置文件或仓库；请在可信本地终端输入。引导仅允许空账号表。管理员创建后，在界面创建查询员与复核员。不要编辑数据库哈希来绕过正常账号管理。

工位导入证据仍使用原来的 --Import 命令，但管理模式下必须先完成管理表升级。CLI 为本机运维入口，数据库目录权限就是其边界，不应交给普通网站用户。

## 3. HTTPS 与服务运行

后端默认只绑定 127.0.0.1:43111；对外使用发布包的 Caddyfile.example，替换为真实 DNS 主机名，再由已安装的 Caddy 运行该配置。DNS、80/443入口和证书签发条件需在目标环境确认。不要把模板域名当成已部署地址。

生产配置的会话及 CSRF Cookie 使用 Secure，浏览器必须从 HTTPS 入口登录。客户端、静态文件与API保持同源，代理转发到 loopback；没有开放跨域凭据。

后端只信任本机代理的 X-Forwarded-Proto；不解析 X-Forwarded-For，保留loopback连接对端用于本机入口限制。代理若更换部署拓扑，必须同步重新核对信任边界。

Windows 上可由已有服务管理器或计划任务在系统启动后执行 Run-Trace.ps1。使用固定的低权限服务账号，指定正确工作目录与 PowerShell7绝对路径，退出非零时交给运行策略告警/重启；先验证就绪再接入访问流量。本工具不替机器安装服务或修改系统证书信任。

服务账号只读程序目录、读写数据目录。Data Protection 密钥位于数据目录的 keys，必须与数据库一起纳入受控备份。此示例显式写入文件系统密钥，没有额外密钥加密器；限制目录ACL与备份访问。迁移到另一台机器前应按目标部署方案配置证书或密钥保护。

## 4. 健康与备份

- /health/live 是进程存活检查。
- /health/ready 同时检查追溯数据与管理结构；503时不应分配新流量。
- 日志使用请求ID关联错误；不要把密码、Cookie、密钥或完整生产数据库放入公共CI日志。

```powershell
pwsh -File F:\TraceReleases\v1\Run-Trace.ps1 -Mode Backup -BackupPath F:\TraceBackups\snapshot-001.db
```

备份目标必须不存在。SQLite在线快照包含检测、证据、复核、账号、工位与审计表。密钥目录需另行备份，选择备份时点并确认服务身份可读。仅复制活动的 .db 文件不能替代快照工具。

## 5. 升级与回退

1. 记录当前程序版本、配置、数据库结构版本、证书与密钥位置。
2. 生成数据库快照与密钥备份，保留旧发布目录。
3. 将快照恢复到新的测试目录；先对副本运行升级和就绪检查。
4. 维护窗口停止写入并生成最终快照，再按已验证步骤切换。
5. 失败时保留失败副本，使用另一份已知兼容快照与原发布目录恢复，不覆盖失败现场。

数据回退会丢弃快照之后的写入，必须先核对复核和事件投递。不要让旧程序直接连接未知的新结构，也不要将版本号改小冒充回退。

恢复快照可能让账号停用状态和版本回到过去，使旧Cookie重新有效。恢复到兼容结构后，在接入流量前执行会话撤销，要求用户重新登录：

```powershell
pwsh -File F:\TraceReleases\v1\Run-Trace.ps1 -Mode RevokeSessions
```

该本机维护命令在事务中增加所有账号版本并记录审计，不修改密码。演练先核对保留密钥时Cookie确实可解密，再执行撤销并确认旧Cookie被拒绝、重新登录成功。

## 6. 自动演练范围

`npm run verify:trace -- --deployment` 在临时目录实际发布，用只存在于测试目录的证书建立 HTTPS 代理。Node客户端显式信任该证书，未关闭全局TLS验证。演练验证Secure Cookie、备份拒绝覆盖、索引升级、未知管理版本拒绝启动以及快照回退。

它使用Node本机代理验证链路，没有安装/运行目标机Caddy，没有真实公网证书，也不代表Windows服务开机启动和现场持续运行已验收。这些是目标环境的明确交付检查项。
