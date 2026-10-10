import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getArticles } from "@/lib/content/queries";
import { buildTagIndex } from "@/lib/content/tags";

export const dynamicParams = false;
function tags() { return buildTagIndex(getArticles()); }
export function generateStaticParams() { return tags().map((tag) => ({ slug: tag.slug })); }

export async function generateMetadata({ params }: { params: Promise<{ slug: string }> }): Promise<Metadata> {
  const { slug } = await params;
  const tag = tags().find((item) => item.slug === slug);
  if (!tag) notFound();
  return { title: `${tag.name} · 知识标签`, description: `探索 ${tag.name} 相关的 ${tag.articles.length} 篇已发布知识文章。` };
}

export default async function TagPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = await params;
  const tag = tags().find((item) => item.slug === slug);
  if (!tag) notFound();
  return <div className="container-shell py-12"><nav aria-label="标签位置" className="mb-5 text-xs text-secondary"><Link href="/tag/" className="hover:text-accent">知识标签</Link><span aria-hidden="true"> / </span>{tag.name}</nav><p className="eyebrow mb-3">Knowledge tag</p><h1 className="text-3xl font-semibold break-words">{tag.name}</h1><p className="mt-3 text-sm text-secondary">{tag.articles.length} 篇已发布文章</p><div className="mt-9 grid gap-4 md:grid-cols-2">{tag.articles.map((article) => <article key={article.id} className="panel flex min-w-0 flex-col p-5 sm:p-6"><p className="text-xs text-secondary">{article.breadcrumbs.map((item) => item.title).join(" / ")}</p><h2 className="mt-3 text-lg font-semibold"><Link href={article.href} className="hover:text-accent">{article.title}</Link></h2><p className="mt-3 flex-1 text-sm leading-7 text-secondary">{article.description}</p><p className="mt-5 border-t border-border pt-4 text-xs text-secondary">更新于 <time dateTime={article.updated}>{article.updated}</time></p></article>)}</div></div>;
}
