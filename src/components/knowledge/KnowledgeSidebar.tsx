import Link from "next/link";
import { ChevronDown } from "lucide-react";
import type { NavigationNode } from "@/types/content";

function NavigationBranch({ nodes, activeHref }: { nodes: NavigationNode[]; activeHref: string }) {
  return <ul className="space-y-1">{nodes.map((node) => <li key={node.path}>
    {node.kind === "article" && node.href ? (
      <Link href={node.href} prefetch={false} aria-current={node.href === activeHref ? "page" : undefined} className={`block rounded-md px-3 py-2 text-sm leading-6 ${node.href === activeHref ? "bg-accent-soft font-medium text-accent" : "text-secondary hover:bg-surface-hover hover:text-primary"}`}>{node.title}</Link>
    ) : (
      <div className="py-2"><p className="mb-2 px-3 text-xs font-semibold text-secondary">{node.title}</p><div className="ml-3 border-l border-border pl-2"><NavigationBranch nodes={node.children} activeHref={activeHref} /></div></div>
    )}
  </li>)}</ul>;
}

export function KnowledgeSidebar({ tree, activeHref }: { tree: NavigationNode; activeHref: string }) {
  const navigation = <nav aria-label={`${tree.title}知识导航`} className="mt-3"><NavigationBranch nodes={tree.children} activeHref={activeHref} /></nav>;
  const title = <span className="flex items-center justify-between gap-3 text-sm font-semibold">{tree.title} <ChevronDown size={16} className="text-secondary" /></span>;
  return <>
    <details className="panel p-4 lg:hidden"><summary>{title}</summary>{navigation}</details>
    <aside className="hidden lg:block"><details open className="sticky top-24 max-h-[calc(100vh-120px)] overflow-y-auto rounded-lg border border-border bg-surface p-4"><summary>{title}</summary>{navigation}</details></aside>
  </>;
}
