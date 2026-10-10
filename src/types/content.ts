export type TocItem = { id: string; title: string; depth: 2 | 3 };

export type Article = {
  toc: TocItem[];
  id: string;
  title: string;
  description: string;
  tags: string[];
  difficulty: "beginner" | "intermediate" | "advanced";
  status: "draft" | "published";
  updated: string;
  order: number;
  related: string[];
  prerequisite: string[];
  slug: string[];
  href: string;
  breadcrumbs: { title: string; path: string }[];
};

export type NavigationNode = {
  kind: "directory" | "article";
  title: string;
  path: string;
  order: number;
  href?: string;
  id?: string;
  children: NavigationNode[];
};

export type ContentIndex = {
  articles: Article[];
  navigation: NavigationNode[];
};
