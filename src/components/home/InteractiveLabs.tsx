import Link from "next/link";
import { ArrowUpRight, FlaskConical } from "lucide-react";
import type { getHomeLabs } from "@/lib/content/home";

export function InteractiveLabs({ labs }: { labs: ReturnType<typeof getHomeLabs> }) {
  return <section id="labs" aria-labelledby="labs-title" className="mt-14">
    <div className="mb-6"><p className="eyebrow mb-2">Interactive labs</p><h2 id="labs-title" className="text-2xl font-semibold tracking-tight">交互实验室</h2><p className="mt-3 max-w-2xl text-sm leading-7 text-secondary">把运行过程变成可以观察的实验。PDO 实验已开放，其他演示仍在筹备。</p></div>
    <div className="grid gap-4 md:grid-cols-3">{labs.map((lab) => <article key={lab.id} className="panel flex min-w-0 flex-col p-5 sm:p-6">
      <div className="mb-6 flex items-center justify-between gap-3"><FlaskConical size={20} className="text-accent" aria-hidden="true" /><span className="rounded border border-border px-2 py-1 text-xs text-secondary">{lab.status === "available" ? "现已开放" : "筹备中 · 尚未开放"}</span></div>
      <p className="mb-2 font-mono text-[10px] tracking-wider text-secondary uppercase">{lab.english}</p><h3 className="text-base font-semibold">{lab.title}</h3><p className="mt-3 flex-1 text-sm leading-7 text-secondary">{lab.description}</p>
      <div className="mt-6 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4"><span className="text-xs text-secondary">{lab.topic}</span><div className="flex flex-wrap items-center gap-3">{lab.reading ? <Link href={lab.reading.href} className="inline-flex items-center gap-2 text-xs text-accent" aria-label={`阅读基础知识：${lab.reading.title}`}>阅读基础知识 <ArrowUpRight size={13} aria-hidden="true" /></Link> : <span className="text-xs text-secondary">基础知识待收录</span>}{lab.status === "available" && <Link href={lab.href} className="inline-flex items-center gap-1.5 rounded-lg bg-accent px-3 py-2 text-xs font-medium text-background">进入实验 <ArrowUpRight size={13} aria-hidden="true" /></Link>}</div></div>
    </article>)}</div>
  </section>;
}
