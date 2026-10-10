import type { Article, NavigationNode } from "../../types/content";

export type HomeCategory = { path: string; title: string; count: number; technologies: { path: string; title: string; count: number }[] };

/** Metadata only. Homepage discovery never includes drafts, even in development. */
export function buildHomeOverview(articles: Article[], navigation: NavigationNode[], recentLimit = 5) {
  const published = articles.filter((article) => article.status === "published");
  const counts = new Map<string, number>();
  for (const article of published) {
    for (const key of [article.slug[0], article.slug.slice(0, 2).join("/")]) counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  const categories: HomeCategory[] = navigation.filter((node) => node.kind === "directory" && counts.has(node.path)).map((node) => ({
    path: node.path, title: node.title, count: counts.get(node.path)!,
    technologies: node.children.filter((child) => child.kind === "directory" && counts.has(child.path)).map((child) => ({ path: child.path, title: child.title, count: counts.get(child.path)! })),
  }));
  const recent = [...published].sort((a, b) => b.updated.localeCompare(a.updated) || a.href.localeCompare(b.href)).slice(0, Math.max(0, recentLimit));
  return { categories, recent, articleCount: published.length, technologyCount: categories.reduce((count, category) => count + category.technologies.length, 0) };
}

export const plannedLabs = [
  { id: "ethercat-frame", title: "PDO 过程数据实验", english: "PDO data flow lab", description: "调整从站数量、周期和示例映射，观察过程数据随帧流动与过程映像偏移。", topic: "EtherCAT", articleId: "ethercat-pdo", status: "available", href: "/labs/ethercat-pdo/" },
  { id: "async-task", title: "异步任务的执行路径", english: "Async task simulator", description: "逐步观察异步等待与方法恢复，把代码和执行过程对应起来。", topic: "C#", articleId: "csharp-async-await", status: "planned" },
  { id: "tcp-connection", title: "TCP 连接的建立", english: "Connection visualizer", description: "跟随握手消息与两端状态，理解连接建立的三个步骤。", topic: "Network", articleId: "tcp-handshake", status: "planned" },
] as const;

export function getHomeLabs(articles: Article[]) {
  const publishedById = new Map(articles.filter((article) => article.status === "published").map((article) => [article.id, article]));
  return plannedLabs.map((lab) => ({ ...lab, reading: publishedById.get(lab.articleId) }));
}
