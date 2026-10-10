# 搜索使用与发布

## 使用

点击顶栏搜索图标，或按 Ctrl/Cmd + K。输入中英文关键词，使用 ↑ / ↓ 选择、Enter 打开、Esc 关闭；Tab 在弹窗内移动。关闭后焦点回到原入口。

生产搜索覆盖已发布文章的标题、简介、标签、分类和渲染正文（含代码）。结果显示分类、标题、命中摘要，首批最多十条，更多结果按需加载。草稿不被检索。

## 开发与生产

`npm run dev` 会同步更新内容元数据，但不会生成静态全文索引。开发弹窗仅检索标题、简介、标签、分类，底部有明确提示；不使用上次构建的旧索引。

验证完整正文搜索：停止 dev，再运行：

```sh
npm run build
npm start
```

`prebuild` 准备已发布内容，Next 导出 HTML，`postbuild` 自动运行 `npm run build:search`。Pagefind 失败会使整个构建失败。修改文章后需重新构建，不能只上传新 HTML 而保留旧索引。

## 索引边界

`pagefind.yml` 只扫描 `out/knowledge/**/*.html`。只有文章页的 `data-pagefind-body` 区域进入索引，目录页、侧栏、TOC、页脚、复制按钮与行号排除。标题/简介/分类/标签通过 `data-pagefind-meta` 提供，分类标签额外作为索引属性。

保留 `# + .` 等技术词字符，适配 C# 等标识。Pagefind Extended 引擎支持中文分词；具体词语的召回仍应通过发布版本验证。

## 部署

完整发布 `out/`，不要漏掉或过滤 `out/pagefind/` 的 JS、WASM、分片与清单。静态托管应正常提供模块 JS 与 WebAssembly MIME 类型，资源不存在时返回 404，不要重写成首页 HTML。

当前部署在站点根路径，引擎地址为 `/pagefind/pagefind.js`。子路径部署尚未配置；需同时调整 Next basePath、搜索引擎地址及结果 URL 校验，再执行部署验证。

搜索在浏览器本地执行，查询不发送到搜索服务；浏览器按需下载静态索引文件。公开静态索引不是隐私边界：不要将保密内容设为 published。

## 故障处理与维护

索引加载失败时界面提供重试按钮；确认构建成功、Pagefind 目录已发布、资源没有被错误缓存或重写，再重试或刷新。首次检索会下载引擎；后续复用已加载实例。

适配层将摘要拆成文本与 mark，不注入 HTML，且只允许本站 `/knowledge/` 结果。新增正文组件时，应决定其文本是否应被搜索；操作按钮等辅助内容使用 `data-pagefind-ignore` 排除。

运行 `npm run test:content` 验证 URL 安全、摘要转换、分批加载和开发预览；发布前另外检查真实正文命中、中文检索、草稿隔离、键盘与手机布局。API 与索引约定参考 [Pagefind API](https://pagefind.app/docs/api/) 和 [索引范围](https://pagefind.app/docs/indexing/)。
