"use client";

import { useId, useState } from "react";
import type { QuizData } from "@/lib/interactive/schema";
import { gradeAnswer } from "@/lib/interactive/state";

export function QuizClient({ question, options, answer, explanation }: QuizData) {
  const id = useId();
  const [selected, setSelected] = useState<number | null>(null);
  const phase = gradeAnswer(selected, answer);
  const detail = explanation ?? `正确答案是“${options[answer]}”。`;
  return <section className="atlas-learning atlas-quiz" aria-labelledby={`${id}-question`}>
    <p className="atlas-learning-eyebrow" data-pagefind-ignore>知识自测</p>
    <p id={`${id}-question`} className="atlas-learning-title">{question}</p>
    <div data-pagefind-ignore>
      <div className="atlas-quiz-options" role="group" aria-label="选择答案">
        {options.map((option, index) => <button key={option} type="button" aria-pressed={selected === index} onClick={() => setSelected(index)} data-result={selected === index ? phase : undefined}><span aria-hidden="true" className="atlas-option-letter">{String.fromCharCode(65 + index)}</span><span>{option}</span></button>)}
      </div>
      <div role="status" aria-live="polite" aria-atomic="true" className="atlas-quiz-feedback" data-result={phase}>
        {phase === "idle" ? <p>选择一项，立即查看判断与解释。</p> : <><p className="atlas-feedback-label">{phase === "correct" ? "✓ 回答正确" : "再想一想"}</p>{phase === "incorrect" && <p>正确答案：{options[answer]}</p>}<p>{detail}</p></>}
      </div>
      {selected !== null && <button type="button" className="atlas-learning-button" onClick={() => setSelected(null)}>重新作答</button>}
    </div>
    <details className="atlas-learning-reference"><summary data-pagefind-ignore>查看答案与解释</summary><p>正确答案：{options[answer]}</p><p>{detail}</p></details>
  </section>;
}
