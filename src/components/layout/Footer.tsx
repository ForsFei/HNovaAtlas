import { Compass } from "lucide-react";

export function Footer() {
  return (
    <footer className="mt-20 border-t border-border py-7 text-xs text-secondary">
      <div className="container-shell flex flex-wrap items-center justify-between gap-4">
        <span className="flex items-center gap-2"><Compass size={15} /> HNova Atlas · 个人技术知识地图</span>
        <span>理解原理，连接知识，持续构建。</span>
      </div>
    </footer>
  );
}
