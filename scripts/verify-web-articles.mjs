import { readdir, readFile } from "node:fs/promises";
import { evaluate } from "@mdx-js/mdx";
import matter from "gray-matter";
import * as runtime from "react/jsx-runtime";
import { createElement } from "react";
import { renderToStaticMarkup } from "react-dom/server";

// Render trusted repository tutorials, separately from the data-only metadata scanner.
// Native Markdown rendering catches accidental expressions such as an unquoted {id}.
const directory = new URL("../content/web/full-stack/", import.meta.url);
let count = 0;
for (const file of (await readdir(directory)).sort()) {
  if (!file.endsWith(".mdx")) continue;
  const { data, content } = matter(await readFile(new URL(file, directory), "utf8"));
  if (data.status !== "published") continue;
  try {
    const module = await evaluate(content, { ...runtime, development: false });
    renderToStaticMarkup(createElement(module.default));
    count++;
  } catch (error) {
    throw new Error(`${file}: tutorial rendering failed`, { cause: error });
  }
}
console.log(`PASS rendered ${count} published Web tutorials without unresolved MDX expressions.`);
