import Link from "next/link";
import { Compass } from "lucide-react";
import { ThemeSelect } from "./ThemeSelect";
import { MobileNavigation } from "./MobileNavigation";
import { SearchLauncher } from "@/components/search/SearchLauncher";

const links = [
  { href: "/knowledge/", label: "探索知识" },
  { href: "/tag/", label: "知识标签" },
  { href: "/#approach", label: "学习方式" },
  { href: "/about/", label: "关于 Atlas" },
];

export function Header() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-background">
      <div className="container-shell flex min-h-18 items-center justify-between gap-3">
        <Link href="/" className="flex items-center gap-2.5 font-semibold tracking-tight" aria-label="HNova Atlas 首页">
          <span className="flex size-8 items-center justify-center rounded-lg border border-border bg-surface text-accent"><Compass size={20} /></span>
          <span className="hidden min-[360px]:inline">HNova <span className="hidden font-normal text-secondary sm:inline">Atlas</span></span>
        </Link>
        <nav aria-label="主导航" className="hidden items-center gap-7 text-sm text-secondary md:flex">
          {links.map((link) => <Link key={link.href} href={link.href} className="hover:text-primary">{link.label}</Link>)}
        </nav>
        <div className="flex items-center gap-2">
          <SearchLauncher />
          <ThemeSelect />
          <MobileNavigation links={links} />
        </div>
      </div>
    </header>
  );
}
