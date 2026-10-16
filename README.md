# HNova Atlas

个人技术知识库。原始九阶段 MVP 已实现至 Phase 9：真实 Markdown / MDX、自动知识导航、文章阅读体验、数据驱动首页、静态全文搜索、MDX 正文与交互学习组件，并完成主题、响应式与性能专项优化。

## 本地运行

需要 Node.js 22.12 或更高版本。

```sh
npm install
npm run dev
```

访问 http://localhost:43110。Windows 也可双击项目根目录的 `start-atlas.cmd` 快速启动开发服务（首次使用先运行 `npm install`）。dev 自动准备内容并监听 content 变化。关闭终端或按 Ctrl+C 停止服务。

## 验证与静态预览

```sh
npm run lint
npm run typecheck
npm run test:content
npm run test:csharp-articles
npm run build
npm start
```

`build` 导出 `out/` 并自动生成 Pagefind 搜索索引，`start` 使用静态服务器预览该目录。部署需完整发布 `out/`（含 `pagefind/`），无需 Node 服务。
开发服务与静态预览均使用 43110 端口，二者不能同时运行。

## 当前范围

- App Router + TypeScript Strict + Tailwind v4。
- CSS Variables 集中定义颜色、阅读宽度、圆角和动画时长。
- 深色 / 浅色 / 跟随系统主题，保存用户选择。
- 首页领域概览、关于页面、404、桌面与移动导航。
- 键盘焦点、跳过导航入口、减少动画偏好。

已发布文章数量与知识导航由内容目录自动生成；开发草稿不会进入生产站点。`test:csharp-articles` 会单独编译标注为完整 `Program.cs` 的部分 C# 教程代码块，尚不覆盖所有说明性片段。

文章页包含自动 TOC、当前章节提示、语法高亮、代码复制、文件名、行号、行强调、知识顺序 Previous/Next 和关联知识。桌面三栏，手机折叠目录与代码局部滚动。文章标签可点击，`/tag/` 汇总已发布文章的标签与相关内容。
首页展示真实领域/技术入口、已发布统计和最近五条更新。交互实验室已有 `http://localhost:43110/labs/ethercat-pdo/` 的 PDO 过程数据实验；异步任务与 TCP 实验仍处于筹备状态。实验操作与模拟边界见 [PDO 实验说明](docs/pdo-lab.md)。
全局搜索支持 Ctrl/Cmd + K、中文与英文、命中摘要高亮、方向键选择和 Esc 关闭。生产检索标题、简介、标签、分类与正文；开发模式只提供已发布元数据预览。详见 [搜索指南](docs/search.md)。
MDX 已支持 Callout、Tabs/Tab、Accordion、构建端 CodeBlock 和按可视区域加载的 Mermaid。PDO 与 WPF 文章包含实际示例，作者用法见 [组件指南](docs/mdx-components.md)。
Phase 8 提供 Quiz 即时答题反馈、FlashCard 翻卡、Stepper 步骤与状态展示，已用于 PDO 与 TCP 文章。状态仅存在于当前页面；静态文字后备和全文搜索保留答案与完整步骤。详见 [学习组件指南](docs/interactive-components.md)。
Phase 9 对长文章目录滚动定位和密集知识导航预取做了优化，并核查主题与响应式行为；设计与性能边界见 [Phase 9 记录](docs/performance.md)。原始九阶段已结束，后续功能按独立需求规划。

## 结构

- `src/app`：页面与共享布局。
- `src/components/layout`：导航、页脚、主题控件。
- `src/components/home`：首页五个展示模块（服务端组件）。
- `src/lib/content/home.ts`：首页聚合与筹备实验目录。
- `src/styles/tokens.css`：设计 Token。
- `task_plan.md`、`findings.md`、`progress.md`：实施与验证记录。
- `docs/architecture.md`：架构与阶段边界。
- `docs/content-authoring.md`：目录、Frontmatter、草稿与开发更新指南。
- `scripts/content-pipeline.ts`：内容校验与构建准备。
- `src/generated/`：自动生成文件，不提交 Git。
- `tests/home.test.ts`：首页统计、排序、草稿过滤与实验阅读链接测试。
- `src/components/search`、`src/lib/search`：懒加载搜索界面与引擎适配。
- `pagefind.yml`：静态索引范围与技术标识字符配置。
- `tests/search.test.ts`：结果分批、URL 边界、安全摘要与开发搜索测试。
- `docs/mdx-components.md`：组件契约、键盘、图表与安全约定。
- `tests/mdx.test.tsx`：组件静态语义、Tabs 校验/键盘、Mermaid 策略与围栏转换测试。
- `src/components/interactive`、`src/lib/interactive`：学习组件、严格数据校验与局部状态。
- `tests/interactive.test.tsx`：数据边界、静态后备、步骤边界、多实例标识与 MDX 示例回归测试。
- `src/lib/content/toc-position.ts`、`tests/performance.test.ts`：长文章目录的对数级章节定位与测试。
- `src/lib/content/tags.ts`、`src/app/tag/`、`tests/tags.test.ts`：标签聚合、静态页面和草稿隔离测试。
- `src/app/labs/ethercat-pdo/`、`src/components/labs/`、`src/lib/labs/`：PDO 教学实验页、交互与纯数据模型。

开发与生产准备共用生成目录；生产构建前停止 dev。构建完成后用 npm start 查看发布版本。

尚未配置真实站点域名、GitHub 链接、Sitemap 或发布流水线。
