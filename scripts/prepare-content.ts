import { prepareContent } from "./content-pipeline";
import { access } from "node:fs/promises";

let needed = true;
if (process.argv.includes("--if-missing")) {
  try {
    await access("src/generated/content-index.json");
    await access("src/generated/article-loaders.ts");
    needed = false;
  } catch { /* Generate on a clean checkout. */ }
}
if (needed) await prepareContent(!process.argv.includes("--development"));
