import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { getArticle, getArticleLoader, getArticles, getTechnologyTree } from "@/lib/content/queries";
import { KnowledgeSidebar } from "@/components/knowledge/KnowledgeSidebar";
import { TableOfContents } from "@/components/knowledge/TableOfContents";
import { ReadingFooter } from "@/components/knowledge/ReadingFooter";
import { getReadingLinks } from "@/lib/content/relationships";
import { tagHref } from "@/lib/content/tags";

export const dynamicParams = false;
export function generateStaticParams() { return getArticles().map((article) => ({ slug: article.slug })); }

export async function generateMetadata({ params }: { params: Promise<{ slug: string[] }> }): Promise<Metadata> {
  const article = getArticle((await params).slug);
  if (!article) notFound();
  return { title: article.title, description: article.description, ...(article.status === "draft" ? { robots: { index: false, follow: false } } : {}) };
}

const difficultyNames = { beginner: "入门", intermediate: "进阶", advanced: "高级" };

export default async function KnowledgeArticlePage({ params }: { params: Promise<{ slug: string[] }> }) {
  const { slug } = await params;
  const article = getArticle(slug);
  const loader = getArticleLoader(slug);
  if (!article || !loader) notFound();
  const { default: Content } = await loader();
  const tree = getTechnologyTree(article);
  const readingLinks = getReadingLinks(article, getArticles(), tree);

  return <div className="container-shell grid items-start gap-6 py-8 lg:grid-cols-[220px_minmax(0,1fr)] xl:grid-cols-[220px_minmax(0,1fr)_180px] lg:gap-8">
    {tree && <KnowledgeSidebar tree={tree} activeHref={article.href} />}
    <article data-pagefind-body={article.status === "published" ? "" : undefined} data-category={article.breadcrumbs.map((item) => item.title).join(" / ")} data-tags={article.tags.join(" ")} data-pagefind-meta="category[data-category], tags[data-tags]" data-pagefind-index-attrs="data-category,data-tags" className="min-w-0 max-w-[var(--reading-width)] lg:col-start-2 lg:row-start-1">
      <nav aria-label="知识位置" className="mb-6 flex flex-wrap items-center gap-2 text-xs leading-6 text-secondary">
        <Link href="/knowledge/" className="hover:text-accent">知识库</Link>
        {article.breadcrumbs.map((item) => <span key={item.path} className="flex items-center gap-2"><span aria-hidden="true">/</span><Link href={`/knowledge/#${item.path}`} className="hover:text-accent">{item.title}</Link></span>)}
      </nav>
      <header className="mb-8 border-b border-border pb-7">
        <div className="mb-3 flex items-center gap-3 text-xs text-secondary"><span>{difficultyNames[article.difficulty]}</span>{article.status === "draft" && <span className="rounded bg-accent-soft px-2 py-1 text-warning">草稿 · 开发预览</span>}</div>
        <h1 data-pagefind-meta="title" className="text-3xl leading-snug font-semibold tracking-tight">{article.title}</h1>
        <p data-pagefind-meta="description" className="mt-3 text-base leading-7 text-secondary">{article.description}</p>
        <div className="mt-5 flex flex-wrap items-center gap-3 text-xs text-secondary"><span>更新于 <time dateTime={article.updated}>{article.updated}</time></span>{article.tags.map((tag) => article.status === "published" ? <Link key={tag} href={tagHref(tag)} prefetch={false} className="rounded border border-border px-2 py-1 hover:border-accent hover:text-accent">{tag}</Link> : <span key={tag} className="rounded border border-border px-2 py-1">{tag}</span>)}</div>
      </header>
      <div data-pagefind-ignore className="xl:hidden"><TableOfContents items={article.toc} /></div>
      <div className="knowledge-prose"><Content /></div>
      <ReadingFooter {...readingLinks} />
    </article>
    <div className="hidden xl:block xl:col-start-3 xl:row-start-1 xl:sticky xl:top-24"><TableOfContents items={article.toc} variant="sidebar" /></div>
  </div>;
}
