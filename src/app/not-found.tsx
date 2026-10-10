import Link from "next/link";

export default function NotFound() {
  return <div className="container-shell py-24"><p className="eyebrow mb-4">404</p><h1 className="text-3xl font-semibold">这条路径还没有知识。</h1><p className="mt-4 text-secondary">页面可能已移动，或尚未收录。</p><Link href="/" className="mt-8 inline-block text-sm text-accent">返回首页 →</Link></div>;
}
