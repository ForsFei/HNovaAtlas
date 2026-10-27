import type { Metadata } from "next";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { buildRoadmap } from "@/lib/content/roadmap";
import { RoadmapView } from "@/components/roadmap/RoadmapView";

export const metadata: Metadata = { title: "学习路径" };

export default function RoadmapPage() {
  const tracks = buildRoadmap();
  return (
    <article className="container-shell py-16">
      <div className="max-w-[var(--reading-width)]">
        <p className="eyebrow mb-4">Learning roadmap</p>
        <h1 className="text-3xl font-semibold tracking-tight">学习路径</h1>
        <p className="mt-4 leading-8 text-secondary">
          两条完整路线：先用 Python 学会编程，再用生活类比理解计算机基础。按顺序学即可——每篇都标注了前置依赖，勾选你已完成的文章，进度保存在本地浏览器。
        </p>
      </div>
      <div className="mt-12">
        <RoadmapView tracks={tracks} />
      </div>
      <Link href="/" className="mt-14 inline-flex items-center gap-2 text-sm text-accent">
        <ArrowLeft size={16} /> 返回知识首页
      </Link>
    </article>
  );
}
