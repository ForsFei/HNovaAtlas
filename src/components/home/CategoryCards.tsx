import Link from "next/link";
import { ArrowUpRight, Braces, CircuitBoard, Layers, Monitor, Network, type LucideIcon } from "lucide-react";
import type { HomeCategory } from "@/lib/content/home";

const presentations = new Map<string, { icon: LucideIcon; english: string; description: string }>([
  ["programming", { icon: Braces, english: "Programming", description: "从语言机制到内存与并发，建立扎实的编程基础。" }],
  ["desktop", { icon: Monitor, english: "Desktop development", description: "理解数据绑定、线程模型与桌面应用的设计。" }],
  ["industrial", { icon: CircuitBoard, english: "Industrial automation", description: "连接软件与设备，探索通信、控制和实时系统。" }],
  ["computer-science", { icon: Network, english: "Computer science", description: "追溯底层原理，把分散的技术知识连接起来。" }],
]);

export function CategoryCards({ categories }: { categories: HomeCategory[] }) {
  return <section id="explore" aria-labelledby="explore-title" className="pt-12">
    <div className="mb-7 flex flex-wrap items-end justify-between gap-3"><div><p className="eyebrow mb-2">Explore</p><h2 id="explore-title" className="text-2xl font-semibold tracking-tight">探索知识领域</h2></div><Link href="/knowledge/" className="inline-flex items-center gap-2 text-sm text-secondary hover:text-accent">浏览全部知识 <ArrowUpRight size={14} aria-hidden="true" /></Link></div>
    {!categories.length ? <p className="panel p-6 text-sm text-secondary">知识地图尚未收录已发布文章，内容发布后会自动出现在这里。</p> : <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">{categories.map((category) => {
      const presentation = presentations.get(category.path) ?? { icon: Layers, english: category.path, description: "在主题与知识点之间，建立新的理解。" };
      const Icon = presentation.icon;
      return <article key={category.path} className="panel flex min-w-0 flex-col p-6">
        <div className="mb-6 flex items-center justify-between"><Icon size={23} className="text-accent" aria-hidden="true" /><span className="rounded border border-border px-2 py-1 text-xs text-secondary">{category.count} 篇笔记</span></div>
        <p className="mb-2 font-mono text-[10px] tracking-wider text-secondary uppercase">{presentation.english}</p>
        <h3 className="text-lg font-semibold"><Link href={`/knowledge/#${category.path}`} className="hover:text-accent">{category.title}</Link></h3>
        <p className="mt-3 flex-1 text-sm leading-7 text-secondary">{presentation.description}</p>
        <ul className="mt-6 flex flex-wrap gap-2 border-t border-border pt-4" aria-label={`${category.title}技术入口`}>{category.technologies.map((technology) => <li key={technology.path}><Link href={`/knowledge/#${technology.path}`} className="inline-flex items-center gap-2 rounded border border-border px-2.5 py-1.5 text-xs text-secondary hover:border-accent hover:text-accent">{technology.title}<ArrowUpRight size={12} aria-hidden="true" /></Link></li>)}</ul>
      </article>;
    })}</div>}
  </section>;
}
