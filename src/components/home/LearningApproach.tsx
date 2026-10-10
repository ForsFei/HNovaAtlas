import { BookOpen, Layers, Route } from "lucide-react";

export function LearningApproach() {
  return <section id="approach" aria-labelledby="approach-title" className="mt-14 grid gap-8 border-t border-border pt-10 md:grid-cols-[1fr_2fr]">
    <div><p className="eyebrow mb-2">Built for understanding</p><h2 id="approach-title" className="text-2xl font-semibold">不止于记录</h2><p className="mt-3 text-sm leading-7 text-secondary">让笔记成为可以阅读、关联与探索的知识。</p></div>
    <div className="grid gap-6 sm:grid-cols-3">{[{ icon: BookOpen, title: "系统阅读", text: "沿着领域与主题组织内容，让每个知识点都有清晰的上下文。" }, { icon: Layers, title: "连接概念", text: "通过相关知识与前置概念，把理解逐步连接成体系。" }, { icon: Route, title: "交互学习", text: "逐步加入图示、练习与演示，帮助理解文字之外的运行过程。" }].map(({ icon: Icon, title, text }) => <div key={title}><Icon size={19} className="mb-4 text-secondary" aria-hidden="true" /><h3 className="mb-2 text-sm font-semibold">{title}</h3><p className="text-sm leading-7 text-secondary">{text}</p></div>)}</div>
  </section>;
}
