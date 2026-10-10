import Link from "next/link";
import type { Article } from "@/types/content";

export function ReadingFooter({ previous, next, related }: { previous?: Article; next?: Article; related: Article[] }) {
  if (!previous && !next && !related.length) return null;
  return <footer className="mt-12 border-t border-border pt-8">
    {(previous || next) && <nav aria-label="知识顺序导航" className="grid gap-3 sm:grid-cols-2">{previous ? <Link href={previous.href} className="panel p-4 hover:border-accent"><span className="text-xs text-secondary">← 上一篇</span><span className="mt-2 block text-sm">{previous.title}</span></Link> : <span />}{next && <Link href={next.href} className="panel p-4 sm:text-right hover:border-accent"><span className="text-xs text-secondary">下一篇 →</span><span className="mt-2 block text-sm">{next.title}</span></Link>}</nav>}
    {!!related.length && <section className="mt-8" aria-labelledby="related-knowledge"><h2 id="related-knowledge" className="mb-4 text-lg font-semibold">关联知识</h2><div className="grid gap-3 sm:grid-cols-2">{related.map((item) => <Link key={item.id} href={item.href} className="panel p-4 hover:border-accent"><h3 className="text-sm font-medium">{item.title}</h3><p className="mt-2 text-xs leading-6 text-secondary">{item.description}</p></Link>)}</div></section>}
  </footer>;
}
