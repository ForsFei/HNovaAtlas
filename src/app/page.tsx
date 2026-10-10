import { getArticles, getNavigation } from "@/lib/content/queries";
import { buildHomeOverview, getHomeLabs } from "@/lib/content/home";
import { HomeHero } from "@/components/home/HomeHero";
import { CategoryCards } from "@/components/home/CategoryCards";
import { RecentUpdates } from "@/components/home/RecentUpdates";
import { InteractiveLabs } from "@/components/home/InteractiveLabs";
import { LearningApproach } from "@/components/home/LearningApproach";

export default function HomePage() {
  const articles = getArticles();
  const overview = buildHomeOverview(articles, getNavigation());
  const example = articles.find((article) => article.id === "ethercat-pdo" && article.status === "published") ?? overview.recent[0];
  return <div className="container-shell">
    <HomeHero articleCount={overview.articleCount} domainCount={overview.categories.length} technologyCount={overview.technologyCount} example={example} />
    <CategoryCards categories={overview.categories} />
    <RecentUpdates articles={overview.recent} />
    <InteractiveLabs labs={getHomeLabs(articles)} />
    <LearningApproach />
  </div>;
}
