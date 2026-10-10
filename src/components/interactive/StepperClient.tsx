"use client";

import { useId, useRef, useState } from "react";
import type { StepperData, StepData } from "@/lib/interactive/schema";
import { clampStep } from "@/lib/interactive/state";

function States({ states }: { states: StepData["states"] }) {
  return states.length ? <dl className="atlas-step-states">{states.map(({ label, value }) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl> : null;
}

export function StepperClient({ title, steps }: StepperData) {
  const id = useId();
  const [active, setActive] = useState(0);
  const panel = useRef<HTMLDivElement>(null);
  function go(index: number) { setActive(clampStep(index, steps.length)); panel.current?.focus({ preventScroll: true }); }
  const step = steps[active];
  return <section className="atlas-learning atlas-stepper" aria-labelledby={`${id}-title`}>
    <p className="atlas-learning-eyebrow" data-pagefind-ignore>逐步观察</p>
    <p id={`${id}-title`} className="atlas-learning-title">{title}</p>
    <div data-pagefind-ignore>
      <ol className="atlas-step-nav" aria-label="选择步骤">{steps.map((item, index) => <li key={index}><button type="button" aria-current={active === index ? "step" : undefined} onClick={() => go(index)}><span aria-hidden="true">{index + 1}</span>{item.title}</button></li>)}</ol>
      <div ref={panel} tabIndex={-1} className="atlas-step-panel" role="region" aria-label={`第 ${active + 1} 步：${step.title}`}>
        <p className="atlas-step-count" role="status" aria-live="polite">第 {active + 1} / {steps.length} 步 · {step.title}</p>
        <p className="atlas-learning-text">{step.description}</p><States states={step.states} />
      </div>
      <div className="atlas-step-controls"><button type="button" className="atlas-learning-button" disabled={active === 0} onClick={() => go(active - 1)}>上一步</button><button type="button" className="atlas-learning-button" disabled={active === steps.length - 1} onClick={() => go(active + 1)}>下一步</button><button type="button" className="atlas-learning-button" disabled={active === 0} onClick={() => go(0)}>从头开始</button></div>
    </div>
    <details className="atlas-learning-reference"><summary data-pagefind-ignore>阅读全部步骤</summary><ol>{steps.map((item, index) => <li key={index}><strong>{item.title}</strong><p className="atlas-learning-text">{item.description}</p><States states={item.states} /></li>)}</ol></details>
  </section>;
}
