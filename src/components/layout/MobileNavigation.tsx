"use client";

import Link from "next/link";
import { Menu } from "lucide-react";
import { useRef } from "react";

export function MobileNavigation({ links }: { links: ReadonlyArray<{ href: string; label: string }> }) {
  const disclosure = useRef<HTMLDetailsElement>(null);

  function close() {
    if (disclosure.current) disclosure.current.open = false;
  }

  return (
    <details ref={disclosure} className="relative md:hidden" onKeyDown={(event) => {
      if (event.key === "Escape") {
        close();
        disclosure.current?.querySelector("summary")?.focus();
      }
    }}>
      <summary aria-label="主导航菜单" className="rounded-lg border border-border p-2.5 text-secondary"><Menu size={17} /></summary>
      <nav aria-label="移动导航" className="panel absolute right-0 top-12 w-44 p-2">
        {links.map((link) => <Link key={link.href} href={link.href} onClick={close} className="block rounded-md px-3 py-3 text-sm hover:bg-surface-hover">{link.label}</Link>)}
      </nav>
    </details>
  );
}
