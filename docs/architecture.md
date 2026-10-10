# HNova Atlas 架构与阶段边界

## 确定的方向

Next.js App Router + React + TypeScript Strict + Tailwind v4。第一阶段静态导出，不依赖数据库与部署后的 Node 运行时。

内容采用 Git 管理的 Markdown / MDX。接下来的内容准备脚本扫描目录、校验 Frontmatter，生成构建端文章导入映射、元数据、导航和 TOC。发布路由通过 generateStaticParams 枚举。

目录组织为领域 / 技术 / 主题 / 知识点；小型 _meta.yml 只描述当前目录名称和顺序。文章 id 用于稳定引用，路径生成 URL，order 控制同目录顺序。

MDX 使用 @next/mdx 编译，正文默认构建端处理，交互组件局部使用 Client Components。大型组件只在使用页面加载。

搜索选择 Pagefind，在 Next.js 静态导出完成后索引 out/。Command Palette 使用 API 获取结果，不将整站正文传给客户端。

## 已实现：Phase 2

- 基础项目配置、静态导出。
- 全局语义 Token 和 Tailwind 映射。
- next-themes 三态主题。
- 响应式共享布局、主页领域展示、关于页与 404。

Phase 2 建立了基础布局。

## 当前已实现：Phase 3

- content/ 中收录六篇已发布 Markdown / MDX 和一篇开发草稿。
- scripts/content-pipeline.ts：Frontmatter Schema、目录扫描、引用校验、草稿过滤。
- src/generated/：构建自动生成的元数据与明确 MDX 导入映射，不提交 Git。
- 自动文章路由、知识浏览与技术目录导航；首页显示真实计数。
- scripts/dev.ts：监听内容目录变化，更新导航与路由元数据。
- tests/content.test.ts：验证草稿过滤、排序、重复标识、日期与引用错误。

扫描器对元数据进行校验，不执行正文。Next.js 负责正常编译可信 MDX 模块，Server Components 在构建时渲染。

## 当前已实现：Phase 4

- 共享 remark 标题插件同时驱动内容 AST 索引与正文锚点，顶层 H2/H3 自动生成 TOC。
- Shiki 构建端高亮，CSS Variables 语法主题；代码源码独立传给小型客户端复制按钮。
- 阅读页桌面三栏、手机折叠目录；目录被动滚动监听合并为 animation frame，标示当前章节。
- Previous/Next 使用当前技术导航树深度优先顺序；Related 优先显式引用，再按技术和标签确定性推荐。
- 13 个测试涵盖标题、代码标记、原始复制源码、顺序导航和草稿隔离。

## 当前已实现：Phase 5

- 首页拆为 Hero、CategoryCards、RecentUpdates、InteractiveLabs、LearningApproach，全部构建端渲染，无新增客户端依赖。
- 首页聚合仅采用 published 元数据，即使开发索引有草稿也不混入；领域和技术来自导航路径，统计由文章自动计算。
- 最近五条更新按 updated 降序，同日按 URL 稳定排序，不依赖浏览器当前时间。
- 实验室展示三项明确标为筹备中的计划，通过稳定文章 id 解析基础知识入口；缺失或草稿文章不输出链接。
- 18 个测试覆盖内容与首页；支持空知识库、新领域、目录改名及文章移动。

## 当前已实现：Phase 6

- npm postbuild 在静态导出后运行 Pagefind，只索引带 data-pagefind-body 的已发布知识文章。
- 标题、简介、分类、标签作为元数据可检索，正文包含代码；目录、页脚、代码工具栏与行号不入索引。
- Header 的小型客户端入口监听 Ctrl/Cmd + K，打开时才加载搜索弹窗；输入后才加载 Pagefind 引擎与索引。
- 原生 dialog 提供模态焦点隔离，支持方向键、Enter、Esc、关闭后焦点恢复及中文输入组合事件保护。
- 180ms 防抖与取消标记防止旧结果覆盖；首批最多读取十条摘要，支持加载更多、空结果、失败重试。
- 摘要转换为安全文本与 mark 节点，不执行返回 HTML；结果 URL 限制到本站知识路由。
- dev 使用单独的已发布最小元数据预览，明确不含正文，生产引擎不依赖该预览文件。
- 23 个测试涵盖内容、首页与搜索适配。部署和开发差异见 search.md。

## 当前已实现：Phase 7

- src/mdx-components.tsx 集中映射 Callout / Tabs / Tab / Accordion / Mermaid，普通围栏仍映射到构建端 Shiki CodeBlock。
- Callout 与 Accordion 为 Server Components；Accordion 使用原生 details / summary，无客户端状态。
- Tabs 服务端检查子项 value / label / 唯一性与默认值，内容作为 ReactNode 传入客户端小型切换器；useId 隔离多实例，左右键/Home/End 自动激活。
- remark-mermaid 在编译期将 Mermaid 围栏转换为字符串属性组件；不执行图表字符串。仅文档顶层标题进入 TOC。
- Mermaid 使用 IntersectionObserver 触发动态模块下载，源码与图注静态输出，图形随主题重新生成；渲染队列隔离全局配置竞争，旧请求不覆盖新图。
- 图表采取保守语法子集、配置/HTML/资源拒绝、strict 模式、字符/边数限制和 DOMPurify SVG 净化；不绑定点击函数，动态 SVG 不参与静态索引。
- Mermaid 12 使用顶层 htmlLabels 配置与显式 dagre，Node 运行要求提升为 >=22.12；无远程字体/CDN 依赖。
- 30 项测试覆盖内容、首页、搜索与组件；PDO / WPF 内容直接演示组件，未新增虚构实验。

## 当前已实现：Phase 8

- Quiz / FlashCard / Stepper 通过全局 MDX 注册，服务端严格 Zod 校验纯数据；客户端只保存当前组件临时状态，无新依赖或进度数据库。
- Quiz 保持 question / options / answer(零起始) 契约，支持即时判断、解释和重做；FlashCard 使用原生按钮翻卡；Stepper 展示有界步骤、状态与直接跳转。
- 所有实例 useId 隔离，原生按钮支持键盘，反馈/步数礼貌播报，步骤切换焦点进入状态区。
- 原生 details 保留答案、卡片两面和全部步骤；知识文字静态索引，交互副本/操作反馈通过 data-pagefind-ignore 排除。
- PDO 示例自测与双卡片；TCP 文章从 .md 改为 .mdx，网址/id 保持不变，展示 RFC 接收后快照。完整协议可视化实验仍筹备中。
- 38 项测试包含数据边界、静态后备、安全转义、多实例、步骤边界和真实 MDX 示例。API 与状态生命周期见 interactive-components.md。

## 当前已实现：Phase 9

- 沿用 next-themes、语义 Token、原生移动导航和减少动画偏好，回归三态主题与多尺寸布局。
- TOC 在可见断点更新，缓存标题 DOM 引用并以二分查找定位当前章节，避免每个滚动帧线性读取全部标题位置；底部最后一节规则保留。
- 知识浏览和技术侧栏的密集静态文章链接关闭自动预取，避免大目录中预取大量完整文章；少量阅读顺序链接保留默认行为。
- 搜索、图表既有懒加载与纯静态导出保持不变。测量方法、取舍和扩容限制见 performance.md。

原始 Phase 1–9 已完成。SEO 发布策略、标签聚合以及更完整的实验室为后续独立工作；首个真实 PDO 实验见下节。

## 后续独立阶段：PDO 交互实验

- 静态 `/labs/ethercat-pdo/` 页面嵌入局部 Client Component，不引入图形库或服务器依赖。
- 纯数据模型计算 1–5 台相同示例从站的 RxPDO/TxPDO 字段宽度与独立输入/输出过程映像偏移；周期控件只做频率算术换算，播放不是网络时序仿真。
- 首页仅将真实 PDO 实验标为开放；PDO 概览、Mapping 与过程映像三篇内容互链。边界与操作见 `docs/pdo-lab.md`。

## 后续独立阶段：标签发现

- `src/lib/content/tags.ts` 从已发布元数据聚合标签，排除草稿并稳定排序；纯 ASCII 标签保留可读 slug，中文/标点使用无碰撞的 UTF-8 编码。
- `/tag/` 和 `/tag/[slug]/` 在构建时静态生成，文章页已发布标签可点击；草稿预览仅显示标签文本，不指向未发布页面。

## 安全边界

MDX 是可执行源代码，只编译人工审核的仓库内容。组件映射不等同于沙箱。未来未经审核的 AI 自动预览优先采用 Markdown + 校验后的结构化组件数据，不直接执行 MDX / JavaScript 字符串。

## 当前维护约定

- 全部语义颜色在 src/styles/tokens.css 定义。
- 默认跟随系统，用户选择保存在 next-themes 管理的本地存储中。
- 不依赖远程字体服务。
- npm run lint 单独运行；Next build 不代替 lint。
- 部署目录为 out/。GitHub 项目 Pages 子路径需额外配置 basePath；当前未配置。
