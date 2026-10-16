# 编写知识内容

## 路径与导航

文章存于 `content/<领域>/<技术>/<主题>/<知识点>.mdx`。主题可以省略，也可以细分更多层级。文件名和目录名使用小写英文、数字与连字符。

路径自动生成 URL，例如 `content/industrial/ethercat/communication/pdo.mdx` 对应 `/knowledge/industrial/ethercat/communication/pdo/`。

每个目录可放 `_meta.yml`：

```yaml
title: 通信机制
order: 20
```

没有描述文件时使用目录名与默认顺序 100。目录中出现文章即可自动进入导航；生产模式会移除只包含草稿的空目录。

目录和文章均按 `order` 升序排列，相同顺序按路径稳定排序。知识首页与技术侧栏使用同一棵树。

## Frontmatter

```yaml
---
id: ethercat-pdo
title: EtherCAT PDO
description: 理解过程数据对象与映射
tags: [EtherCAT, PDO]
difficulty: beginner
status: draft
updated: "2026-09-29"
order: 10
related: [ethercat-sdo]
prerequisite: [ethercat-introduction]
---
```

- 必填：`id`、`title`、`description`、`updated`。
- `id` 全站唯一，只使用小写英文、数字和连字符；移动文章后保持 id 不变。
- `status` 为 draft / published，省略时默认为 draft。
- `difficulty` 为 beginner / intermediate / advanced，默认为 beginner。
- `tags`、`related`、`prerequisite` 默认为空数组；`order` 默认为 100。
- 分类从路径推导，不再重复填写 category / section。
- `related` / `prerequisite` 引用 id，而不是文件名或 URL；引用必须存在。已发布文章不能引用未发布草稿。
- 未识别字段和无效日期会阻止生成。错误信息包含文件路径。

页面已根据 title 生成 H1，正文从 `##` 开始。支持 `.md` 和 `.mdx`，同路径不能同时存在这两种扩展名，否则 URL 冲突。

## 开发与发布

```sh
npm run dev
```

启动时自动生成开发索引，并监听 content。新增、删除、移动文章和修改目录描述后，无需重启；保存后等待终端 `[content]` 输出和 Next.js 热更新完成。移动过程中可能短暂出现编译提示，文件与索引更新完成后会恢复。

草稿在开发模式显示，并附带草稿标记与 noindex。索引校验失败时，终端报错，保留上一次有效索引，修复后保存即可重新生成。

```sh
npm run prepare:content
npm run test:content
npm run lint
npm run typecheck
npm run build
npm start
```

`prepare:content` 默认生成生产索引，`build` 自动先运行它。生产模式在生成文章导入映射前排除草稿，导出页面、首页计数与导航都只包含已发布文章。

当前开发与构建共用 `src/generated/`。先停止开发服务器，再运行生产内容准备或构建；重新运行 dev 会恢复包含草稿的开发索引。

生成文件不提交 Git。全新检出运行 dev / build 会生成；typecheck 在文件缺失时自动补齐。

## 内容安全与当前能力

内容扫描器读取 YAML，并解析正文 AST 提取目录，但不执行 MDX；生产模式不解析草稿正文。仅允许以独立的 `---` 行开始的 YAML Frontmatter，拒绝 JavaScript Frontmatter 引擎。

但 MDX 正文仍是可执行源代码。只编译已审核的仓库内容，AI 生成内容先作为待审草稿；不要把未经审核的文件导入编译链路。开发模式也会编译草稿。

目前支持正文、链接、列表、GFM 表格、代码高亮和 JSX，以及自动目录、复制、知识导航、静态全文搜索与标签聚合。Phase 7 提供 Callout、Tabs/Tab、Accordion 与 Mermaid，详见 [MDX 组件指南](mdx-components.md)。Phase 8 提供 Quiz / FlashCard / Stepper，详见 [学习组件指南](interactive-components.md)。使用 JSX 组件的文章必须采用 `.mdx` 扩展名；普通 `.md` 用于纯 Markdown。

## 文章目录与代码块

正文顶层 `##` 和 `###` 自动进入 TOC，锚点统一使用 `section-` 前缀；重复标题有数字后缀。代码围栏内或 JSX 容器内的标题不进入目录，避免链接到隐藏内容。标题文本变化会改变锚点。

代码围栏可写为：

````md
```csharp filename="PageReader.cs" {3,5-7}
// 原始代码
```
````

`filename` 可选，双引号包围；`{3,5-7}` 表示强调第 3、5 至 7 行。行号自动显示且对辅助阅读隐藏，复制按钮使用原始源码，不含行号、文件名或按钮文字。浏览器需支持安全上下文 Clipboard API（localhost / HTTPS）；失败时显示手动选择提示。

Shiki 在构建端处理，语法颜色由 tokens.css 统一控制并随主题切换。未知语言降级为纯文本。支持 Shiki 内置语言，例如 csharp、xml、json、javascript、typescript、python、bash。

上一篇/下一篇按当前技术目录深度优先的知识顺序排列（目录和文章的 order），不按更新时间。关联知识优先使用 `related` 的显式顺序，随后按同技术与共同标签推荐，最多四篇且不推荐草稿或当前文章。

### 系列课程示例：C#

`content/programming/csharp/overview.mdx` 是课程入口；`fundamentals`、`object-oriented`、`collections`、`reliability`、`concurrency` 的 `_meta.yml` 决定大章节顺序，文章的 `order` 决定章内顺序。新增 C# 课程文章时，同时更新总览的阅读表和对应的 `prerequisite` / `related` ID。代码示例应标明是完整 `Program.cs` 还是接在前一个片段中的局部示例；标准控制台示例可用 `examples/csharp-reading-analyzer` 对照验证。该系列刻意以正文、代码和练习为主，不为每个概念添加交互组件。

修改本地 remark / rehype 插件实现后应重新编译内容；Turbopack 的 MDX 缓存可能不追踪插件文件本身。改变插件配置或重新保存相关内容会使编译签名更新。

## 首页展示规则

- 首页统计、领域卡片和最近更新只显示 `status: published` 内容，开发草稿仍可从开发知识浏览页进入。
- 领域顺序、技术名称与顺序遵循 `_meta.yml`；新增领域会自动显示通用图标与介绍。不需要修改文章索引。
- 最近更新默认取五篇，使用 `updated` 日期降序，同日按 URL 排序。修改正文不会自动改变日期，需要同步更新 Frontmatter；日期显示为绝对日期，静态站点不会产生过期的“几天前”。
- Hero 的知识结构示例优先使用 `ethercat-pdo` 稳定 id，找不到已发布文章时回退到最近更新第一篇；空知识库显示写作引导。
- 领域的图标和介绍在 `src/components/home/CategoryCards.tsx`，以领域路径为键，目录改名不会影响映射。
- 实验目录在 `src/lib/content/home.ts` 的 `plannedLabs`。`articleId` 只生成“阅读基础知识”入口，不能把普通文章标成可运行实验；关联文章删除或变为草稿后，卡片显示“基础知识待收录”。真实实验另需 `status: "available"` 与有效静态 `href`，目前只有 PDO 实验开放，其他卡片仍为筹备状态。
- 文章 `tags` 会自动生成 `/tag/` 聚合和独立静态标签页，只有已发布文章参与。简单 ASCII 标签使用小写可读路径（如 `EtherCAT` → `/tag/ethercat/`），中文或带符号标签使用稳定 UTF-8 编码路径；不要手写或猜测后者的 URL，统一从文章页进入。草稿专属标签不会发布。

首页由构建端生成，发布新增内容后需要重新 build / 部署；不需要 CMS、数据库或浏览器加载完整文章正文。
