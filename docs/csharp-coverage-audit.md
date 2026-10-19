# C# 课程覆盖审查（2026-10-15）

## 口径

这份清单按站内八章的实际 MDX 小节审查，不把“标题出现了一个词”当作已深入讲解。目标是服务基础复习、进阶理解和面试准备；八章是主线，不等于整个 .NET 平台、ASP.NET、EF Core 或语言规范的全部内容。每篇保持「规则 → 可运行示例/可观察反例 → 使用边界 → 自测」的写法，避免独立的面试背诵栏和不必要交互。

## 本批发现与处理

第 47 批补[record 继承与 with](/knowledge/programming/csharp/object-oriented/record-inheritance-and-with/)：承接 `class/struct/record` 的基本复制与相等，验证记录继承中的运行时类型比较、基类视图下的 `with`、派生属性的静态可见性和可变数组浅复制。`examples/csharp-record-hierarchy` 与正文完整代码纳入编译；不将 record 自动相等误说成序列相等或深度不可变。


第 46 批补[.NET 结构化日志：模板、异常与作用域](/knowledge/programming/csharp/reliability/structured-logging-and-scopes/)：继追踪与指标后，用可控 `ILogger` 替身验证级别筛选、EventId、模板原文与类型化字段、异常对象及作用域释放。`examples/csharp-structured-logging` 和正文完整代码纳入编译；区分 ILogger 提交的结构化状态与具体 provider 的输出、采集、脱敏及并发语义。


第 45 批补[.NET 指标：Counter、Histogram 与当前值](/knowledge/programming/csharp/reliability/metrics-counters-histograms-and-gauges/)：在 ActivitySource 单次追踪之后，以 `MeterListener` 离线验证计数增量、耗时原始样本、按需观测的当前值及低基数结果标签。`examples/csharp-metrics` 与正文完整代码纳入编译；不把原始测量误当收集端的速率、百分位或已导出的监控数据。

第 44 批补[ActivitySource 与追踪](/knowledge/programming/csharp/reliability/activity-source-and-tracing/)：通过 `ActivityListener` 离线验证父子操作、共享 TraceId、独立 SpanId、跨 await 的当前活动与显式错误状态。`examples/csharp-activity-tracing` 及正文完整代码纳入编译；强调无监听者可返回 null、仪表化不等于导出、活动关联不等于线程相同或异常已处理。

第 43 批补[ref struct、scoped 与 stackalloc](/knowledge/programming/csharp/fundamentals/ref-struct-scoped-and-stackalloc/)：承接 Span/Memory 篇的视图与租期，专门解释引用安全、限制逃逸、有界栈上分配和 C# 13+ 的 async 局部作用域。`examples/csharp-ref-safety` 通过自定义字节游标与小/大缓冲分支验证行为，正文完整代码纳入编译；不把 ref struct 误说成“底层必在栈上”，也不将 stackalloc 宣传为必然更快。

第 42 批补[HTTP 端到端截止时间与取消](/knowledge/programming/csharp/io/http-end-to-end-deadline-and-cancellation/)：承接上批只在下一步前检查的预算门槛，用一次链接令牌贯穿单次 GET 的发送和正文读取。`examples/csharp-http-end-to-end-deadline` 以阻塞响应头处理器、阻塞正文流和短计时器分别验证调用方取消与在途截止的四种组合；正文完整代码纳入编译。明确范围不是多次重试、完整下载或服务器操作回滚。

第 41 批补[HTTP Retry-After 与预算](/knowledge/programming/csharp/io/http-retry-after-and-budget/)：承接仅 GET/503 的有限立即重试，加入 503 秒数提示、429 日期提示、剩余五秒预算门槛和等待中取消。`examples/csharp-http-retry-budget` 使用可控 `TimeProvider` 与响应/等待替身，无真实网络或睡眠；正文完整代码块纳入编译。明确该示例只控制下一步是否开始，不是覆盖在途发送与正文读取的完整截止时间；生产还需端到端取消与统一策略。


第 40 批补[不可变集合与快照边界](/knowledge/programming/csharp/collections/immutable-collections-and-snapshots/)：把集合选型篇的只读视图提醒展开成可运行对照。`examples/csharp-immutable-snapshots` 断言 `AsReadOnly()` 会反映源列表变化、数组/`ImmutableArray<T>` 保留容器内容、`Add` 返回新值、默认数组与 `Empty` 不同，以及可变元素仍可被修改；正文完整代码块纳入编译。强调不可变容器不是深拷贝或并发更新协议，且逐项 `Add` 不适合大批量构建。


第 39 批补[JSON 多态契约与类型辨别符](/knowledge/programming/csharp/collections/json-polymorphism-and-discriminators/)：承接基础 JSON 契约，显式登记温度与心跳两种派生消息，用 `$kind` 验证往返、未知辨别符和缺失必需字段，并把空白设备 ID 留给业务校验。`examples/csharp-json-polymorphism` 的确定性断言及正文完整代码块纳入编译；强调类型允许列表不等于认证、授权或输入大小限制。


第 38 批补[静态抽象接口成员与泛型数学](/knowledge/programming/csharp/generics/static-abstract-members-and-generic-math/)：承接约束篇的 `INumber<T>` 加法速览，定义最小加法契约与带单位的 `Energy`，再验证空输入、整数除法截断与小数平均值。`examples/csharp-static-abstract-math` 的断言和正文完整代码块纳入编译；强调类型约束只保证操作可用，不保证溢出、精度或领域单位契约。


第 37 批补[显式接口实现与默认接口成员](/knowledge/programming/csharp/object-oriented/explicit-and-default-interface-members/)：承接抽象类/接口篇，用导出与审计两个同名 `Render()` 契约验证显式接口映射，用默认版本方法验证接口视图可调用但不自动进入类公开 API。`examples/csharp-interface-dispatch` 的确定性断言与正文完整代码块纳入编译；强调显式实现不是授权边界，默认实现也不等于类继承。


第 36 批补[HTTP 重试与幂等边界](/knowledge/programming/csharp/io/http-retry-and-idempotency/)：承接单次下载测试，用 `examples/csharp-http-retry` 的注入式处理器验证 GET 的 503→200、400 不重试、连续 503 的次数上限、预取消与响应释放；正文完整代码块纳入编译。不把即时重试示例当生产退避策略，也不推断 POST 可安全自动重试；写请求需明确服务端幂等协议与结果查询。


第 35 批补[自定义运算符与转换契约](/knowledge/programming/csharp/fundamentals/user-defined-operators-and-conversions/)：承接内置运算符、类型转换和值相等，用非负 `ByteCount` 说明 `+` 的不变量和 checked 溢出、`==`/`!=` 成对声明、`Equals`/`GetHashCode` 一致以及可能失败的转换为何显式。`examples/csharp-operator-contracts` 验证负数、溢出、默认零和 `HashSet` 去重；正文完整代码块纳入编译。强调运算符仅用于语义清楚的值运算，不把所有业务动作符号化。


第 34 批补[文件完整发布与失败清理](/knowledge/programming/csharp/io/complete-file-publication/)：把异步文件篇的“同目录临时文件再发布”建议落实为新建目标的可运行案例。`examples/csharp-file-publication` 用确定性本地文件测试成功、目标重名、长度错误、写入一块后的取消以及临时文件清理；正文完整代码块纳入编译。明确不覆盖旧目标、不宣称跨文件系统原子性或断电持久性，替换/备份需另设协议。


第 33 批补[LINQ 连接与键语义](/knowledge/programming/csharp/collections/linq-joins-and-key-semantics/)：承接 LINQ 基础与集合相等性，用同一批设备/读数说明内连接、分组连接、左连接、空组与重复键。`examples/csharp-linq-joins` 验证大小写比较器及 .NET 10 `LeftJoin` 与传统 `GroupJoin` + `DefaultIfEmpty` 的等价输出；正文完整代码块纳入编译。示例只证明 LINQ to Objects，不把比较器和排序规则直接推断为 ORM 的 SQL 行为。


第 32 批补[`IAsyncDisposable` 与 `await using`](/knowledge/programming/csharp/reliability/async-disposal-and-ownership/)：把已有 GC/I/O/异步流篇使用的异步清理语法展开为可观察的生命周期契约。`examples/csharp-async-disposal` 用闸门验证作用域等待、主体异常后的清理、借用者不释放；正文完整代码块纳入编译。不把成功调用 `DisposeAsync` 解释为 GC 立即回收或外部副作用自动回滚。


第 31 批补[Unicode 文本边界](/knowledge/programming/csharp/fundamentals/unicode-text-elements-and-normalization/)：把原字符串篇的一句 `Length` 提示展开为 UTF-16 代码单元、`Rune` 标量值、`StringInfo` 文本元素和 Form C 规范等价比较。`examples/csharp-unicode-text` 用固定转义字面量断言代理对与组合音调，正文完整代码块纳入编译；不将文本元素数当显示宽度，也不把规范化当安全审核。


第 30 批补[Task 组合的故障与超时](/knowledge/programming/csharp/concurrency/task-composition-faults-and-timeouts/)：用受控任务区分 `WhenAll` 的故障/取消优先级及多异常、`WhenAny` 的赢家任务和落败者、`WaitAsync` 等待超时与原任务生命周期。`examples/csharp-task-composition` 与正文完整代码块均可编译运行，不依赖真实耗时；不把等待超时误写成已取消底层操作。


第 29 批补[await 恢复到哪里](/knowledge/programming/csharp/concurrency/synchronization-context-and-configureawait/)：承接异步入门，区分默认捕获、`ConfigureAwait(false)` 跳过回送、已完成任务原地继续，以及独立的 `ExecutionContext`/`AsyncLocal` 传播。`examples/csharp-async-context` 使用可控同步上下文断言投递行为，正文完整代码块纳入编译；不依赖线程号、耗时或真实 UI 框架，并说明同步阻塞只在特定条件下可能死锁。


第 28 批补[Task 与 ValueTask 的契约](/knowledge/programming/csharp/concurrency/task-vs-valuetask/)：把异步入门与异步流/Channel 的 `ValueTask` 用例连接起来，强调默认 Task、单次消费、需要 `WhenAll` 时一次性 `AsTask()`。`examples/csharp-task-valuetask` 用可控缓存命中/未命中验证两种完成形态与组合；正文完整代码块单独编译。示例不宣称 `ValueTask` 一定更快，也不把 Task 包装型示例误当全部 ValueTask 行为。


第 27 批补[数值解析与舍入契约](/knowledge/programming/csharp/fundamentals/numeric-parsing-and-rounding/)：承接 `double`/`decimal` 基础，进一步用固定 `NumberFormatInfo` 证明 `"1,234"` 可解析出两个不同值；示例把协议格式、业务范围与中点舍入分层，明确 `NumberStyles` 不等于完整语法验证。`examples/csharp-numeric-contracts` 与正文完整代码块均可编译运行，不依赖本机文化设置。


第 26 批新增[单元测试：边界用例、替身与集成测试分工](/knowledge/programming/csharp/testing/unit-tests-and-boundaries/)作为第八章：用真实 xUnit 项目测试告警阈值、无效输入和出口故障，讲清 Arrange–Act–Assert、参数化测试与依赖替身。`examples/csharp-unit-testing` 的 `dotnet test` 不访问外部服务；SQLite 篇仍作为集成测试对照。测试项目使用本机 SDK 模板同版本依赖，首次运行需 NuGet 还原。


第 25 批补[异步流、取消与提前退出](/knowledge/programming/csharp/concurrency/async-streams-and-cancellation/)：衔接同步 `yield` 与 `Task`，说明 `IAsyncEnumerable<T>` 的逐项等待、`WithCancellation`/`[EnumeratorCancellation]` 的令牌传递、`await foreach` 提前退出时的异步释放。`examples/csharp-async-streams` 用跟踪资源验证延迟开始、`break` 与取消路径；正文完整代码块单独编译。不把 `Task.Yield` 当真实 I/O 或吞吐测试。


第 24 批补[时间点、偏移与时区](/knowledge/programming/csharp/collections/time-and-time-zones/)：承接 JSON 采样时间契约，区分 `DateTimeOffset` 确定的时间点、本地钟面时间和 `TimeZoneInfo` 地区规则，并用固定 2025 年教学时区断言夏令时缺口与重叠。`examples/csharp-time-zones` 可在任意本机时区重复运行；正文完整代码块纳入编译检查。未来预约的缺口/重叠处理属于业务策略，不能由单个偏移推断。


第 23 批补[可空引用契约与流分析](/knowledge/programming/csharp/fundamentals/nullable-contracts-and-flow-analysis/)：承接 `string?` 入门、构造不变量和现有 JSON 契约篇，示范 `[NotNullWhen(true)]`、`[MemberNotNull]` 与实际运行时守卫如何配合，明确 `!` 只压警告、标注不自动执行验证。`examples/csharp-nullable-contracts` 在警告视为错误下编译运行；正文完整代码块纳入检查，不重复 JSON 反序列化流程。

第 22 批补[异常边界与筛选](/knowledge/programming/csharp/reliability/exception-boundaries-and-filters/)：承接异常入门和参数篇，以注入 I/O 故障验证 `catch when` 匹配/不匹配、包装后的 `InnerException`、同一异常原样重抛和有限数值的 Try 模式。`examples/csharp-exception-boundaries` 与正文代码块均可编译运行；不访问真实文件，也不以异常文本或堆栈行号作断言。

第 21 批补[readonly struct 与防御性复制](/knowledge/programming/csharp/object-oriented/readonly-struct-and-defensive-copies/)：承接已有 `in` 参数和值语义文章，新增可变结构体在只读位置调用非只读成员的可观察反例，并区分 `record struct` 默认可写、`readonly record struct` 与含可变引用字段的浅不可变。`examples/csharp-readonly-struct` 断言行为，正文完整代码块纳入编译检查；不把只读结构体宣传为必然栈分配或零复制。

第 20 批补[Span、Memory 与 ArrayPool](/knowledge/programming/csharp/fundamentals/spans-memory-and-pooling/)：从数组视图延伸到同步/异步缓冲选择和池化租期，强调 `ReadOnlySpan` 不等于底层不可变、`Rent` 长度只是下限、归还后禁用所有别名。`examples/csharp-spans-memory` 使用受控池验证成功与短读路径均归还一次；正文完整代码块也纳入编译检查。示例不以池复用行为或微基准宣称性能收益。

第 19 批补[托管内存、GC 与 IDisposable](/knowledge/programming/csharp/reliability/managed-memory-and-disposal/)：把既有值语义、`using`、事件长引用与流所有权串成“GC 可达性 vs 确定性资源清理”的整体框架；`examples/csharp-memory-lifecycle` 验证异常路径释放、借用不关闭、重复清理及释放后拒绝使用。正文完整代码块纳入编译检查；GC 时机、终结器运行与具体存储位置不作为稳定断言。

第 18 批补[可测试 HTTP 下载](/knowledge/programming/csharp/io/testable-http-download/)：延续已有 `ResponseHeadersRead` 方法片段，新增 `examples/csharp-http-download` 的注入式 HTTP 响应，验证成功、非 2xx、声明长度超限、未知长度实际超限和正文取消；正文完整代码块纳入编译检查。示例不访问外网，仍明确用户 URL/重定向审查、半成品处理及总预算需生产代码单独实现。

第 17 批补[组合与依赖注入](/knowledge/programming/csharp/object-oriented/composition-and-dependency-injection/)：已有接口/抽象/继承文章偏语言机制，本篇用告警规则与通知出口说明构造函数注入、手动装配、测试替身、容器生命周期和资源所有权。`examples/csharp-composition-di` 用断言验证依赖替换；正文完整代码块纳入编译检查，不引入新 UI 或必需的容器包。

第 16 批补[异步管线故障与取消](/knowledge/programming/csharp/concurrency/channel-failure-and-cancellation/)：以单生产者有界 Channel 的故障注入和空通道读取消为两个可确定案例，验证缓冲数据、读端错误、等待取消与写端完成是不同状态。`examples/csharp-channel-failures` 提供可运行断言；正文完整代码块纳入编译检查。生产环境的多写者错误归因、重试与持久交付仍需按业务另行设计。

第 15 批补[集合操作复杂度](/knowledge/programming/csharp/collections/collection-complexity-and-search/)：现有集合篇已覆盖类型用途和比较器，本篇聚焦单次与批量查询、摊还扩容、排序加二分的完整成本，明确复杂度不是计时保证。`examples/csharp-collection-complexity` 用断言验证比较规则、查找、插入位置与容量；正文完整代码块纳入编译检查。

第 14 批补[受控并发测试](/knowledge/programming/csharp/concurrency/controlled-concurrency-tests/)：用 `TaskCompletionSource` 同步点让两个预订都先观察到仅剩一个库存，再放行写入，稳定复现“两次成功、最终库存为零”的业务不变量破坏；修复版在同一把锁里完成检查与扣减。`examples/csharp-concurrency-tests` 提供可运行断言，正文完整代码块也加入 `npm run test:csharp-articles`。示例刻意区分可控逻辑交错与真实线程并行，压力测试仍不能代替协议审查。

第 13 批补[SQLite 本地集成验证](/knowledge/programming/csharp/collections/sqlite-integration-test/)：现有 ADO.NET 篇仍保留 SQL Server 示例及前提；新增 `examples/csharp-sqlite` 使用固定版本 `Microsoft.Data.Sqlite` 和内存连接，真实断言参数化查询、Reader、事务提交、约束失败与回滚。正文注明 SQLite 类型与异步行为不能代替 SQL Server 测试。

第 12 批补[内存可见性与原子操作](/knowledge/programming/csharp/concurrency/visibility-and-atomicity/)：把已有 `lock` 篇的一句话提示深化为 `Interlocked`、CAS 重试、`Volatile` 发布与不可变快照的专题。`examples/csharp-atomicity` 验证计数、上限和快照不变量；文章完整 `Program.cs` 代码块加入 `npm run test:csharp-articles`。压力测试不能证明所有交错安全，因此正文明确测试边界。

第 11 批补[运算符与表达式求值](/knowledge/programming/csharp/fundamentals/operators-and-evaluation/)：不重复空值、转换的入门定义，重点区分表达式分组、操作数调用顺序、短路与数值结果。`examples/csharp-expressions` 以断言验证主要行为；`npm run test:csharp-articles` 现在还会单独编译该篇完整 `Program.cs` 代码块。

第 10 批继续补齐第一章：[枚举与 Flags](/knowledge/programming/csharp/fundamentals/enums-and-flags/)区分命名状态与位组合；[元组与解构](/knowledge/programming/csharp/fundamentals/tuples-and-deconstruction/)解释名称、位置和浅复制；[扩展方法](/knowledge/programming/csharp/fundamentals/extension-methods/)解释静态本质、解析优先级与空接收者。三篇共用 `examples/csharp-foundations-part2` 验证核心行为；`npm run test:csharp-articles` 单独编译这三篇各自标出的完整 `Program.cs` 代码块。其他说明性片段仍不保证可单独编译。

| 主题 | 审查前状态 | 本批处理 |
| --- | --- | --- |
| 数组与字符串 | 类型入门只用数组展示引用复制，字符串只提不可变；缺切片/视图、交错数组、明确比较规则 | 新增 `fundamentals/arrays-and-strings.mdx` |
| 模式匹配 | 流程文章仅有简短 `switch` 示例；缺属性/关系/列表模式和非穷尽边界 | 新增 `fundamentals/pattern-matching.mdx` |
| 属性与索引器 | OOP 文章使用 `private set`、`get` 和 `required`，但尚未统一解释访问器、计算属性及索引器 | 新增 `object-oriented/properties-and-indexers.mdx` |
| 重叠内容 | 空值/转换、集合/LINQ、async/取消、异常/I/O 有入门与深化两层 | 保留各自 URL；新文章用前置和关联链接引导，不迁移原文 |

三篇以 `examples/csharp-foundations-review` 的 .NET 10 项目验证核心行为。单独的说明性代码片段并非全部逐块编译；修改示例时仍应按文章标注的上下文复查。

## 各章后续优先级

| 章 | 已有主干 | 仍值得补的范围 |
| --- | --- | --- |
| 基础 | 类型/空值、转换、数组/字符串、枚举、元组、Span/Memory、ref struct/scoped/stackalloc、模式匹配、表达式求值、自定义运算符、参数、扩展方法、访问控制、yield | 可按实际项目需要深化领域值类型与解析契约，不以枚举全部语法为目标 |
| 面向对象 | 属性、构造、接口/抽象、显式接口映射、默认接口成员、相等性、继承、组合与依赖注入边界 | 实际框架注册、复杂接口演进需结合具体调用方验证 |
| 数据处理 | 集合选型/复杂度、只读视图与不可变快照、LINQ、JSON 基础契约与多态辨别符、XML、ADO.NET、SQLite 可复现集成验证 | 若实际项目选择 SQL Server 等引擎，仍需该引擎专属的隔离环境测试 |
| 并发 | 线程/Task、lock、Interlocked/Volatile、受控竞态测试、并发集合、Channel、取消与故障注入 | 可针对具体外部服务补端到端取消、资源清理与重试测试 |
| 泛型、委托、反射 | 方法/约束、静态抽象成员与泛型数学、变型、闭包/事件、特性/创建 | 实际框架用例或源码阅读可作为进阶补充，不重复语言规则 |
| I/O | 流、编码、路径、异步文件、完整新文件发布、HTTP/TCP 边界、可测试限额下载、HTTP 有限重试、幂等、Retry-After 预算门槛及单次 HTTP 端到端取消 | 覆盖旧文件、续传、重试跨尝试的总截止时间、退避抖动与跨网络故障恢复仍需按应用场景深化 |
| 测试与质量 | xUnit 单元测试、边界与失败用例、手写替身、SQLite 集成对照、ActivitySource 操作追踪、Meter 数值指标 | 真实外部服务、Web 请求、遥测导出或 CI 场景需专属环境与契约测试；GC/分配剖析另需独立工具与负载 |

下批若继续，应按学习痛点选择下一主题；可补明确网络/文件契约的端到端案例，或在明确具体数据库引擎后补其专属隔离环境。不要把“现有 8 章均有文章”误读成“课程已经全面完结”。
