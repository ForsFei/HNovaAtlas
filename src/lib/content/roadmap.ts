import "server-only";
import { getArticles } from "@/lib/content/queries";

export type RoadmapItem = {
  id: string;
  title: string;
  description: string;
  href: string;
  difficulty: "beginner" | "intermediate" | "advanced";
};

export type RoadmapStage = {
  title: string;
  description: string;
  items: RoadmapItem[];
};

export type RoadmapTrack = {
  title: string;
  description: string;
  stages: RoadmapStage[];
};

const pythonStages: Omit<RoadmapStage, "items">[] = [
  { title: "阶段 0 · 入门准备", description: "装好环境，跑通第一个程序，建立对编程的直觉。" },
  { title: "阶段 1 · 基础语法", description: "变量、条件、循环、容器、函数——所有程序的积木。" },
  { title: "阶段 2 · 进阶基础", description: "异常处理、文件读写、面向对象与常用标准库。" },
  { title: "阶段 3 · 实战项目", description: "三个小项目把所学串起来，第一次写出完整程序。" },
  { title: "阶段 4 · 语言进阶", description: "推导式、装饰器、生成器、类型注解、测试与工程化。" },
  { title: "阶段 5 · 方向选择", description: "总览四大方向，各用一篇入门试水，再决定深耕哪条路。" },
];

const pythonStageIds: string[][] = [
  ["python-why", "python-setup", "python-first-program"],
  ["python-variables-types", "python-strings", "python-operators", "python-conditionals", "python-loops", "python-lists", "python-dicts-sets", "python-functions", "python-modules-pip"],
  ["python-errors-exceptions", "python-files", "python-oop-basics", "python-stdlib-essentials"],
  ["python-guess-number", "python-todo-cli", "python-rename-files"],
  ["python-comprehensions", "python-functions-advanced", "python-decorators", "python-generators", "python-typing", "python-venv-projects", "python-testing"],
  ["python-directions-roadmap", "python-crawler-basics", "python-data-pandas", "python-excel-automation"],
];

const csStages: Omit<RoadmapStage, "items">[] = [
  { title: "计算机组成", description: "从厨房类比开始，看懂 CPU、内存、总线和开机接力。" },
  { title: "操作系统", description: "大管家如何管理进程、内存、文件，以及与它对话的 Shell。" },
  { title: "数据结构", description: "数步数的直觉 + 六大结构的摆法与选型。" },
  { title: "计算机网络", description: "从网线到浏览器，打通一次网页请求的全过程。" },
];

const csStageIds: string[][] = [
  ["co-overview", "co-binary", "co-cpu", "co-memory", "co-io-bus", "co-boot", "co-gpu", "co-multitask"],
  ["os-what", "os-process", "os-memory", "os-filesystem", "os-shell"],
  ["ds-intro", "ds-array", "ds-stack-queue", "ds-hashmap", "ds-tree", "ds-graph", "ds-summary"],
  ["net-intro", "net-ip-dns", "net-layers", "net-home-network", "net-tcp-udp", "tcp-handshake", "net-http", "net-https", "net-web-journey"],
];

function resolveStage(meta: Omit<RoadmapStage, "items">, ids: string[]): RoadmapStage {
  const articles = getArticles();
  const byId = new Map(articles.map((article) => [article.id, article]));
  const items = ids.map((id) => {
    const article = byId.get(id);
    if (!article || article.status !== "published") {
      throw new Error(`roadmap: 文章不存在或未发布: ${id}`);
    }
    return {
      id: article.id,
      title: article.title,
      description: article.description,
      href: article.href,
      difficulty: article.difficulty,
    };
  });
  return { ...meta, items };
}

export function buildRoadmap(): RoadmapTrack[] {
  return [
    {
      title: "Python 编程之路",
      description: "从零基础到能选方向的完整路线，全部使用 Python 3，每篇带动手练习与测验。",
      stages: pythonStages.map((stage, index) => resolveStage(stage, pythonStageIds[index])),
    },
    {
      title: "计算机基础之路",
      description: "计算机组成、数据结构与计算机网络，用生活类比讲清硬件、组织与网络的底层逻辑。",
      stages: csStages.map((stage, index) => resolveStage(stage, csStageIds[index])),
    },
  ];
}
