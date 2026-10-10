import type { Metadata } from "next";
import Link from "next/link";
import { getArticles } from "@/lib/content/queries";
import { buildTagIndex } from "@/lib/content/tags";

export const metadata: Metadata = { title: "知识标签", description: "按技术标签探索 HNova Atlas 中已发布的知识文章。" };

export default function TagsPage() {
  const tags = buildTagIndex(getArticles());
  return <div className="container-shell py-12"><p className="eyebrow mb-3">Explore by tag</p><h1 className="text-3xl font-semibold">知识标签</h1><p className="mt-3 max-w-2xl text-sm leading-7 text-secondary">跨越目录，按概念查找已发布的知识文章。</p><div className="mt-9 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">{tags.map((tag) => <Link key={tag.slug} href={`/tag/${tag.slug}/`} prefetch={false} className="panel flex items-center justify-between gap-3 p-5 hover:border-accent"><span className="min-w-0 break-words font-medium">{tag.name}</span><span className="shrink-0 text-xs text-secondary">{tag.articles.length} 篇</span></Link>)}</div>{!tags.length && <p className="mt-8 text-secondary">目前还没有已发布文章的标签。</p>}</div>;
}
