import type { NextConfig } from "next";
import createMDX from "@next/mdx";
import path from "node:path";

const nextConfig: NextConfig = {
  // MDX imports must also enter Next's React Server Component rules in webpack.
  pageExtensions: ["js", "jsx", "ts", "tsx", "md", "mdx"],
  output: "export",
  trailingSlash: true,
  images: { unoptimized: true },
};

const withMDX = createMDX({
  extension: /\.(md|mdx)$/,
  options: {
    remarkPlugins: ["remark-frontmatter", "remark-gfm", path.resolve("src/lib/mdx/remark-headings.mjs"), path.resolve("src/lib/mdx/remark-mermaid.mjs")],
    rehypePlugins: [[path.resolve("src/lib/mdx/rehype-code.mjs"), { lineNumbers: true }]],
  },
});

export default withMDX(nextConfig);
