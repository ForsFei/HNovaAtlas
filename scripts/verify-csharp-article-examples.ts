import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";

// Only articles with complete Program.cs blocks are checked here.
// Explanatory fragments are intentionally not treated as standalone programs.
const articles = [
  "content/programming/csharp/fundamentals/enums-and-flags.mdx",
  "content/programming/csharp/fundamentals/tuples-and-deconstruction.mdx",
  "content/programming/csharp/fundamentals/extension-methods.mdx",
  "content/programming/csharp/fundamentals/operators-and-evaluation.mdx",
  "content/programming/csharp/fundamentals/user-defined-operators-and-conversions.mdx",
  "content/programming/csharp/fundamentals/numeric-parsing-and-rounding.mdx",
  "content/programming/csharp/fundamentals/unicode-text-elements-and-normalization.mdx",
  "content/programming/csharp/collections/collection-complexity-and-search.mdx",
  "content/programming/csharp/collections/immutable-collections-and-snapshots.mdx",
  "content/programming/csharp/collections/linq-joins-and-key-semantics.mdx",
  "content/programming/csharp/collections/time-and-time-zones.mdx",
  "content/programming/csharp/collections/json-polymorphism-and-discriminators.mdx",
  "content/programming/csharp/object-oriented/composition-and-dependency-injection.mdx",
  "content/programming/csharp/object-oriented/explicit-and-default-interface-members.mdx",
  "content/programming/csharp/object-oriented/record-inheritance-and-with.mdx",
  "content/programming/csharp/generics/static-abstract-members-and-generic-math.mdx",
  "content/programming/csharp/concurrency/visibility-and-atomicity.mdx",
  "content/programming/csharp/concurrency/task-vs-valuetask.mdx",
  "content/programming/csharp/concurrency/task-composition-faults-and-timeouts.mdx",
  "content/programming/csharp/concurrency/synchronization-context-and-configureawait.mdx",
  "content/programming/csharp/concurrency/async-streams-and-cancellation.mdx",
  "content/programming/csharp/concurrency/controlled-concurrency-tests.mdx",
  "content/programming/csharp/concurrency/channel-failure-and-cancellation.mdx",
  "content/programming/csharp/io/testable-http-download.mdx",
  "content/programming/csharp/io/http-retry-and-idempotency.mdx",
  "content/programming/csharp/io/http-retry-after-and-budget.mdx",
  "content/programming/csharp/io/http-end-to-end-deadline-and-cancellation.mdx",
  "content/programming/csharp/io/complete-file-publication.mdx",
  "content/programming/csharp/reliability/managed-memory-and-disposal.mdx",
  "content/programming/csharp/reliability/activity-source-and-tracing.mdx",
  "content/programming/csharp/reliability/metrics-counters-histograms-and-gauges.mdx",
  "content/programming/csharp/reliability/structured-logging-and-scopes.mdx",
  "content/programming/csharp/reliability/async-disposal-and-ownership.mdx",
  "content/programming/csharp/fundamentals/spans-memory-and-pooling.mdx",
  "content/programming/csharp/fundamentals/ref-struct-scoped-and-stackalloc.mdx",
  "content/programming/csharp/object-oriented/readonly-struct-and-defensive-copies.mdx",
  "content/programming/csharp/reliability/exception-boundaries-and-filters.mdx",
  "content/programming/csharp/fundamentals/nullable-contracts-and-flow-analysis.mdx",
];

const project = `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
`;

for (const article of articles) {
  const source = await readFile(article, "utf8");
  const blocks = [...source.matchAll(/```csharp filename="Program\.cs"\r?\n([\s\S]*?)\r?\n```/g)];
  if (blocks.length === 0) throw new Error(`${article}: no complete Program.cs block`);
  const code = blocks.at(-1)?.[1];
  if (!code) throw new Error(`${article}: empty Program.cs block`);

  const directory = await mkdtemp(join(tmpdir(), "hnova-csharp-article-"));
  try {
    const articleProject = article.endsWith("/structured-logging-and-scopes.mdx")
      ? project.replace("</Project>", '  <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>\n</Project>')
      : project;
    await writeFile(join(directory, "ArticleExample.csproj"), articleProject);
    await writeFile(join(directory, "Program.cs"), code);
    const result = spawnSync(process.platform === "win32" ? "dotnet.exe" : "dotnet", ["build", "--nologo", "--verbosity", "quiet"], {
      cwd: directory,
      encoding: "utf8",
    });
    if (result.error || result.status !== 0) {
      throw new Error(`${article}: compile failed\n${result.stdout ?? ""}${result.stderr ?? ""}${result.error ?? ""}`);
    }
    console.log(`Compiled ${article}`);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}
