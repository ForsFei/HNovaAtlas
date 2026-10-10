import Link from "next/link";
import { ArrowDown, ArrowUpRight } from "lucide-react";
import type { Article } from "@/types/content";

export function HomeHero({ articleCount, domainCount, technologyCount, example }: { articleCount: number; domainCount: number; technologyCount: number; example?: Article }) {
  const levels = example ? [...example.breadcrumbs.map((item, index) => ({ label: index === 0 ? "领域" : index === 1 ? "技术" : "主题", value: item.title })), { label: "知识点", value: example.title }] : [];
  return <section className="grid items-center gap-10 border-b border-border py-12 lg:grid-cols-[1.25fr_1fr] lg:gap-12 lg:py-16">
    <div>
      <p className="eyebrow mb-6">A personal knowledge atlas</p>
      <h1 className="max-w-2xl text-4xl leading-tight font-semibold tracking-tight sm:text-5xl">让知识有结构，<br /><span className="text-secondary">让理解有路径。</span></h1>
      <p className="mt-6 max-w-xl text-base leading-8 text-secondary">记录技术，探索原理，连接概念。把每一次学习和实践，沉淀为可以持续生长的知识地图。</p>
      <div className="mt-7 flex flex-wrap items-center gap-3"><a href="#explore" className="inline-flex items-center gap-3 rounded-lg bg-accent px-5 py-3 text-sm font-medium text-background hover:opacity-90">探索知识领域 <ArrowDown size={16} aria-hidden="true" /></a><a href="#labs" className="inline-flex items-center gap-2 px-2 py-3 text-sm text-secondary hover:text-primary">查看实验计划 <ArrowUpRight size={16} aria-hidden="true" /></a></div>
      <dl className="mt-7 flex flex-wrap gap-x-7 gap-y-3 border-t border-border pt-5 text-sm">{[{ value: domainCount, label: "知识领域" }, { value: technologyCount, label: "技术方向" }, { value: articleCount, label: "已发布笔记" }].map((stat) => <div key={stat.label} className="flex items-baseline gap-2"><dt className="order-2 text-xs text-secondary">{stat.label}</dt><dd className="font-mono text-lg text-primary">{stat.value}</dd></div>)}</dl>
    </div>
    <div className="atlas-grid panel min-w-0 p-5 sm:p-7" aria-label="知识组织结构示意">
      <div className="mb-6 flex items-center justify-between gap-3"><span className="eyebrow">Knowledge structure</span><span className="text-xs text-secondary">从领域到知识点</span></div>
      {levels.length ? levels.map((node, index) => <div key={`${index}-${node.value}`}>
        {index > 0 && <div className="ml-5 h-4 border-l border-border" aria-hidden="true" />}
        <div className="flex items-center gap-3 rounded-lg border border-border bg-surface px-3 py-3.5"><span className="font-mono text-xs text-accent" aria-hidden="true">{String(index + 1).padStart(2, "0")}</span><span className="w-10 shrink-0 text-xs text-secondary">{node.label}</span><span className="min-w-0 break-words text-sm font-medium">{node.value}</span></div>
      </div>) : <p className="py-8 text-sm leading-7 text-secondary">领域 → 技术 → 主题 → 知识点<br />发布第一篇文章，开始构建你的知识地图。</p>}
      {example && <Link href={example.href} className="mt-5 inline-flex items-center gap-2 text-xs text-accent">从这个知识点开始 <ArrowUpRight size={13} aria-hidden="true" /></Link>}
    </div>
  </section>;
}
