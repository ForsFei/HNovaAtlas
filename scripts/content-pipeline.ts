import { access, readdir, readFile, mkdir, writeFile } from "node:fs/promises";
import path from "node:path";
import matter from "gray-matter";
import { parse } from "yaml";
import { unified } from "unified";
import remarkParse from "remark-parse";
import remarkMdx from "remark-mdx";
import remarkGfm from "remark-gfm";
import { collectHeadings } from "../src/lib/mdx/remark-headings.mjs";
import { directorySchema, frontmatterSchema, identifier } from "../src/lib/content/schema";
import type { Article, ContentIndex, NavigationNode } from "../src/types/content";

const collator = new Intl.Collator("en");
const sortNodes = (a: NavigationNode, b: NavigationNode) => a.order - b.order || collator.compare(a.path, b.path);

/** Only reads metadata. MDX is never evaluated by the scanner. */
export async function scanContent(root: string, production: boolean): Promise<ContentIndex> {
  const articles: Article[] = [];
  const directoryTitles = new Map<string, string>();

  async function walk(segments: string[]): Promise<NavigationNode[]> {
    const directory = path.join(root, ...segments);
    const entries = await readdir(directory, { withFileTypes: true });
    const nodes: NavigationNode[] = [];
    for (const entry of entries.sort((a, b) => collator.compare(a.name, b.name))) {
      if (entry.isSymbolicLink()) throw new Error(`${directory}/${entry.name}: 内容目录不允许符号链接`);
      if (entry.isDirectory()) {
        identifier.parse(entry.name);
        const childSegments = [...segments, entry.name];
        const childPath = childSegments.join("/");
        let meta = { title: entry.name, order: 100 };
        try {
          const raw = await readFile(path.join(directory, entry.name, "_meta.yml"), "utf8");
          meta = directorySchema.parse(parse(raw));
        } catch (error) {
          if (!(error instanceof Error && "code" in error && error.code === "ENOENT")) {
            throw new Error(`${childPath}/_meta.yml: ${String(error)}`);
          }
        }
        directoryTitles.set(childPath, meta.title);
        nodes.push({ kind: "directory", title: meta.title, path: childPath, order: meta.order, children: await walk(childSegments) });
      } else if (/\.(md|mdx)$/.test(entry.name)) {
        if (segments.length < 2) throw new Error(`${entry.name}: 文章必须位于领域/技术目录下`);
        const stem = entry.name.replace(/\.(md|mdx)$/, "");
        identifier.parse(stem);
        const file = path.join(directory, entry.name);
        try {
          const source = await readFile(file, "utf8");
          if (!/^---\r?\n/.test(source)) throw new Error("文章必须以 YAML Frontmatter 开始（独立的 --- 行）");
          const { data, content } = matter(source, { engines: { yaml: (text: string) => parse(text) } });
          const metadata = frontmatterSchema.parse(data);
          const slug = [...segments, stem];
          const article: Article = {
            toc: production && metadata.status === "draft" ? [] : collectHeadings(unified().use(remarkParse).use(remarkMdx).use(remarkGfm).parse(content)),
            ...metadata, slug, href: `/knowledge/${slug.join("/")}/`,
            breadcrumbs: segments.map((_, index) => {
              const location = segments.slice(0, index + 1).join("/");
              return { title: directoryTitles.get(location) ?? segments[index], path: location };
            }),
          };
          articles.push(article);
          nodes.push({ kind: "article", title: article.title, path: slug.join("/"), order: article.order, id: article.id, href: article.href, children: [] });
        } catch (error) {
          throw new Error(`${path.relative(root, file)}: ${String(error)}`);
        }
      }
    }
    return nodes.sort(sortNodes);
  }

  const fullNavigation = await walk([]);
  const ids = new Set<string>();
  const urls = new Set<string>();
  for (const article of articles) {
    if (ids.has(article.id)) throw new Error(`重复文章 id: ${article.id}`);
    if (urls.has(article.href)) throw new Error(`重复文章路由: ${article.href}`);
    ids.add(article.id);
    urls.add(article.href);
  }
  for (const article of articles) {
    for (const id of [...article.related, ...article.prerequisite]) {
      if (!ids.has(id)) throw new Error(`${article.id}: 知识引用不存在: ${id}`);
      if (id === article.id) throw new Error(`${article.id}: 不允许引用自身`);
    }
  }

  const visible = articles.filter((article) => !production || article.status === "published");
  const visibleIds = new Set(visible.map((article) => article.id));
  for (const article of visible) {
    if ([...article.related, ...article.prerequisite].some((id) => !visibleIds.has(id))) {
      throw new Error(`${article.id}: 已发布文章引用了未发布草稿`);
    }
  }
  function prune(nodes: NavigationNode[]): NavigationNode[] {
    return nodes.flatMap((node) => {
      if (node.kind === "article") return node.id && visibleIds.has(node.id) ? [node] : [];
      const children = prune(node.children);
      return children.length ? [{ ...node, children }] : [];
    });
  }
  return { articles: visible, navigation: prune(fullNavigation) };
}

async function writeIfChanged(file: string, content: string) {
  try { if (await readFile(file, "utf8") === content) return; } catch (error) {
    if (!(error instanceof Error && "code" in error && error.code === "ENOENT")) throw error;
  }
  await writeFile(file, content);
}

export async function prepareContent(production: boolean) {
  const root = path.join(process.cwd(), "content");
  const index = await scanContent(root, production);
  const generated = path.join(process.cwd(), "src/generated");
  await mkdir(generated, { recursive: true });
  // Resolve extension explicitly; no runtime filesystem or broad dynamic import context.
  const entries = await Promise.all(index.articles.map(async (article) => {
    const location = article.slug.join("/");
    let extension = ".mdx";
    try { await access(path.join(root, `${location}.mdx`)); } catch { extension = ".md"; }
    return `  ${JSON.stringify(location)}: () => import(${JSON.stringify(`../../content/${location}${extension}`)}),`;
  }));
  await writeIfChanged(path.join(generated, "content-index.json"), `${JSON.stringify(index, null, 2)}\n`);
  const preview = index.articles.filter((article) => article.status === "published").map((article) => ({ id: article.id, url: article.href, title: article.title, description: article.description, tags: article.tags, category: article.breadcrumbs.map((item) => item.title).join(" / ") }));
  await writeIfChanged(path.join(generated, "search-preview.json"), `${JSON.stringify(preview, null, 2)}\n`);
  await writeIfChanged(path.join(generated, "article-loaders.ts"), `// Generated by prepare-content. Do not edit.\nimport "server-only";\nimport type { ComponentType } from "react";\nimport type { MDXProps } from "mdx/types";\n\nexport const articleLoaders: Record<string, () => Promise<{ default: ComponentType<MDXProps> }>> = {\n${entries.join("\n")}\n};\n`);
  console.log(`[content] ${index.articles.length} articles (${production ? "production" : "development"})`);
  return index;
}
