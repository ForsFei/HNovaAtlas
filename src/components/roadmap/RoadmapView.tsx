"use client";

import { useSyncExternalStore } from "react";
import Link from "next/link";
import { CheckCircle2, Circle } from "lucide-react";
import type { RoadmapItem } from "@/lib/content/roadmap";
import { roadmapProgress } from "./progress-store";

const difficultyLabel: Record<RoadmapItem["difficulty"], string> = {
  beginner: "入门",
  intermediate: "进阶",
  advanced: "高级",
};

type StageView = { title: string; description: string; items: RoadmapItem[] };
type TrackView = { title: string; description: string; stages: StageView[] };

export function RoadmapView({ tracks }: { tracks: TrackView[] }) {
  const done = useSyncExternalStore(roadmapProgress.subscribe, roadmapProgress.getDone, roadmapProgress.getEmpty);

  return (
    <div className="space-y-16">
      {tracks.map((track) => {
        const all = track.stages.flatMap((stage) => stage.items);
        const doneCount = all.filter((item) => done.has(item.id)).length;
        const percent = all.length ? Math.round((doneCount / all.length) * 100) : 0;
        return (
          <section key={track.title} aria-labelledby={`track-${track.title}`} className="space-y-6">
            <div className="space-y-3">
              <div className="flex flex-wrap items-end justify-between gap-3">
                <h2 id={`track-${track.title}`} className="text-2xl font-semibold tracking-tight">{track.title}</h2>
                <p className="text-sm text-secondary" aria-live="polite">已完成 {doneCount} / {all.length}（{percent}%）</p>
              </div>
              <p className="text-secondary">{track.description}</p>
              <div className="h-1.5 overflow-hidden rounded-full bg-surface" role="presentation">
                <div className="h-full rounded-full bg-accent transition-[width]" style={{ width: `${percent}%` }} />
              </div>
            </div>
            <ol className="space-y-8">
              {track.stages.map((stage) => {
                const stageDone = stage.items.filter((item) => done.has(item.id)).length;
                return (
                  <li key={stage.title} className="rounded-xl border border-border bg-surface p-5">
                    <div className="flex flex-wrap items-baseline justify-between gap-2">
                      <h3 className="text-lg font-semibold tracking-tight">{stage.title}</h3>
                      <p className="text-sm text-secondary">{stageDone} / {stage.items.length}</p>
                    </div>
                    <p className="mt-1 text-sm text-secondary">{stage.description}</p>
                    <ul className="mt-4 divide-y divide-border border-t border-border">
                      {stage.items.map((item) => {
                        const isDone = done.has(item.id);
                        return (
                          <li key={item.id}>
                            <div className="flex items-start gap-3 py-3">
                              <button
                                type="button"
                                onClick={() => roadmapProgress.toggle(item.id)}
                                aria-pressed={isDone}
                                aria-label={`标记「${item.title}」${isDone ? "未完成" : "已完成"}`}
                                className="mt-0.5 shrink-0 text-accent hover:opacity-75"
                              >
                                {isDone ? <CheckCircle2 size={20} /> : <Circle size={20} className="text-border" />}
                              </button>
                              <div className="min-w-0">
                                <p className="flex flex-wrap items-center gap-2">
                                  <Link href={item.href} className={`font-medium hover:text-accent ${isDone ? "text-secondary line-through decoration-1" : "text-primary"}`}>{item.title}</Link>
                                  <span className="rounded-full border border-border px-2 py-0.5 text-xs text-secondary">{difficultyLabel[item.difficulty]}</span>
                                </p>
                                <p className="mt-1 text-sm leading-6 text-secondary">{item.description}</p>
                              </div>
                            </div>
                          </li>
                        );
                      })}
                    </ul>
                  </li>
                );
              })}
            </ol>
          </section>
        );
      })}
    </div>
  );
}
