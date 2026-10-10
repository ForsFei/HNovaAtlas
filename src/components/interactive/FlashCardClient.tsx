"use client";

import { useId, useState } from "react";
import type { FlashCardData } from "@/lib/interactive/schema";

export function FlashCardClient({ front, back }: FlashCardData) {
  const id = useId();
  const [revealed, setRevealed] = useState(false);
  return <section className="atlas-learning atlas-flashcard" aria-label={`记忆卡：${front}`}>
    <p className="atlas-learning-eyebrow" data-pagefind-ignore>概念记忆卡</p>
    <button type="button" className="atlas-flashcard-face" aria-expanded={revealed} aria-controls={`${id}-answer`} onClick={() => setRevealed((value) => !value)} data-revealed={revealed} data-pagefind-ignore>
      <span className="atlas-flashcard-content" hidden={revealed}>{front}</span>
      <span id={`${id}-answer`} className="atlas-flashcard-content" hidden={!revealed}>{back}</span>
      <span className="atlas-flashcard-hint">{revealed ? "点击返回问题" : "点击查看答案"} · Enter / Space</span>
    </button>
    <details className="atlas-learning-reference"><summary data-pagefind-ignore>阅读文字版</summary><p><strong>{front}</strong></p><p className="atlas-learning-text">{back}</p></details>
  </section>;
}
