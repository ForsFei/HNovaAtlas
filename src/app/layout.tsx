import type { Metadata } from "next";
import { Header } from "@/components/layout/Header";
import { Footer } from "@/components/layout/Footer";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
import "@/styles/globals.css";

export const metadata: Metadata = {
  title: { default: "HNova Atlas · 个人技术知识库", template: "%s · HNova Atlas" },
  description: "连接编程、桌面开发、工业自动化与计算机基础的个人技术知识库。",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="zh-CN" data-scroll-behavior="smooth" suppressHydrationWarning>
      <body>
        <ThemeProvider>
          <a href="#main-content" className="fixed left-4 top-4 z-50 -translate-y-24 rounded-lg bg-accent px-4 py-2 text-background focus:translate-y-0">跳到正文</a>
          <Header />
          <main id="main-content">{children}</main>
          <Footer />
        </ThemeProvider>
      </body>
    </html>
  );
}
