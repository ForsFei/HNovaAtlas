import type { Metadata } from "next";
import Link from "next/link";
import { ArrowUpRight, BookOpen } from "lucide-react";
import { getNavigation, getArticles } from "@/lib/content/queries";
import type { NavigationNode } from "@/types/content";

export const metadata: Metadata = { title: "探索知识", description: "按领域、技术和主题浏览知识。" };

function KnowledgeTree({ nodes }: { nodes: NavigationNode[] }) {
  return <ul className="space-y-3">{nodes.map((node) => <li key={node.path} id={node.path}>
    {node.kind === "article" && node.href ? <Link href={node.href} prefetch={false} className="flex items-center justify-between gap-4 rounded-lg px-3 py-2 text-sm text-secondary hover:bg-surface-hover hover:text-accent"><span>{node.title}</span><ArrowUpRight size={14} aria-hidden="true" /></Link> : <div className="pt-2"><h3 className="mb-2 px-3 text-sm font-semibold">{node.title}</h3><div className="ml-3 border-l border-border pl-2"><KnowledgeTree nodes={node.children} /></div></div>}
  </li>)}</ul>;
}

export default function KnowledgePage() {
  const navigation = getNavigation();
  return <div className="container-shell py-12">
    <p className="eyebrow mb-3">Explore the atlas</p>
    <div className="mb-10 flex flex-wrap items-end justify-between gap-4"><div><h1 className="text-3xl font-semibold">知识领域</h1><p className="mt-3 text-sm leading-7 text-secondary">从领域进入技术，在主题中找到具体知识点。</p></div><span className="text-xs text-secondary">{getArticles().length} 篇知识笔记</span></div>
    <div className="grid items-start gap-5 md:grid-cols-2">{navigation.map((domain) => <section key={domain.path} id={domain.path} className="panel p-5 sm:p-6"><h2 className="mb-5 flex items-center gap-3 border-b border-border pb-4 text-lg font-semibold"><BookOpen size={19} className="text-accent" />{domain.title}</h2><KnowledgeTree nodes={domain.children} /></section>)}</div>
    {!navigation.length && <p className="panel p-8 text-secondary">知识库尚未收录文章。</p>}
  </div>;
}
