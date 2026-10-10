import type { Metadata } from "next";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";

export const metadata: Metadata = { title: "关于 Atlas" };

export default function AboutPage() {
  return (
    <article className="container-shell py-16">
      <div className="max-w-[var(--reading-width)]">
        <p className="eyebrow mb-4">About the atlas</p>
        <h1 className="text-3xl font-semibold tracking-tight">为长期学习建立一张知识地图</h1>
        <div className="mt-8 space-y-6 text-base leading-8 text-secondary">
          <p>HNova Atlas 是一个个人技术知识库，用于整理编程语言、桌面开发、计算机基础与工业自动化中的知识与实践。</p>
          <p>内容围绕领域、技术、主题和知识点组织。普通笔记承载解释与经验，交互式学习页面帮助呈现数据流、状态变化和运行过程。</p>
          <p>知识会随理解不断更新。这里更关注一个概念与其他概念的关系，以及如何把它应用到实际问题中。</p>
        </div>
        <Link href="/" className="mt-10 inline-flex items-center gap-2 text-sm text-accent"><ArrowLeft size={16} /> 返回知识首页</Link>
      </div>
    </article>
  );
}
