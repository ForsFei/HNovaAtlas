"use client";

import { useEffect, useState } from "react";
import { ArrowRight, Pause, Play, RotateCcw, StepForward } from "lucide-react";
import { cyclesPerSecond, getPdoLayout, getPdoMapping, getPdoStage, type MappingPreset, type PdoField } from "@/lib/labs/pdo-model";

function FieldRows({ fields, offset }: { fields: PdoField[]; offset: number }) {
  return <ul className="divide-y divide-border text-sm">{fields.map((field, index) => {
    const currentOffset = offset + fields.slice(0, index).reduce((total, previous) => total + previous.bits / 8, 0);
    return <li key={field.name} className="flex items-center justify-between gap-4 py-2.5"><span>{field.name} <span className="text-xs text-secondary">({field.bits} bit)</span></span><code className="shrink-0 text-xs text-accent">Byte {currentOffset}–{currentOffset + field.bits / 8 - 1}</code></li>;
  })}</ul>;
}

export function PdoLab() {
  const [cycle, setCycle] = useState(1000);
  const [slaveCount, setSlaveCount] = useState(3);
  const [preset, setPreset] = useState<MappingPreset>("basic");
  const [step, setStep] = useState(0);
  const [selectedSlave, setSelectedSlave] = useState(1);
  const [playing, setPlaying] = useState(false);
  const mapping = getPdoMapping(preset);
  const layout = getPdoLayout(slaveCount, preset);
  const stage = getPdoStage(slaveCount, step);
  const selected = layout[selectedSlave - 1] ?? layout[0];
  const outputTotal = layout.reduce((total, slave) => total + slave.outputBytes, 0);
  const inputTotal = layout.reduce((total, slave) => total + slave.inputBytes, 0);

  useEffect(() => {
    if (!playing) return;
    const timer = window.setInterval(() => setStep((current) => current >= slaveCount + 1 ? 0 : current + 1), 900);
    return () => window.clearInterval(timer);
  }, [playing, slaveCount]);

  const restart = () => { setPlaying(false); setStep(0); };
  const stageText = stage.returned ? "帧已返回主站；应用可读取本轮输入过程映像。" : stage.activeSlave ? `经过从站 ${stage.activeSlave}：读取该站的输出数据，并将输入数据写入返回内容。` : "主站准备本轮输出过程数据，并发出示意帧。";

  return <div className="space-y-6">
    <section aria-labelledby="lab-controls" className="panel p-5 sm:p-6">
      <div className="mb-5 flex flex-wrap items-baseline justify-between gap-2"><h2 id="lab-controls" className="text-xl font-semibold">实验设置</h2><p className="text-xs text-secondary">更改配置会回到本轮起点</p></div>
      <div className="grid gap-6 lg:grid-cols-3">
        <div><label htmlFor="pdo-cycle" className="block text-sm font-medium">Cycle Time：{cycle} µs</label><input id="pdo-cycle" type="range" min="250" max="2000" step="250" value={cycle} onChange={(event) => { setCycle(Number(event.target.value)); restart(); }} className="mt-4 w-full accent-accent" /><div className="mt-1 flex justify-between text-xs text-secondary"><span>250 µs</span><span>2000 µs</span></div></div>
        <div><label htmlFor="pdo-slaves" className="block text-sm font-medium">从站数量：{slaveCount}</label><input id="pdo-slaves" type="range" min="1" max="5" step="1" value={slaveCount} onChange={(event) => { const count = Number(event.target.value); setSlaveCount(count); setSelectedSlave((current) => Math.min(current, count)); restart(); }} className="mt-4 w-full accent-accent" /><div className="mt-1 flex justify-between text-xs text-secondary"><span>1 台</span><span>5 台</span></div></div>
        <div><label htmlFor="pdo-preset" className="block text-sm font-medium">示例 PDO 映射</label><select id="pdo-preset" value={preset} onChange={(event) => { setPreset(event.target.value as MappingPreset); restart(); }} className="mt-3 min-h-11 w-full rounded-lg border border-border bg-surface px-3 text-sm"><option value="basic">基础：控制字 / 状态字</option><option value="motion">扩展：加目标 / 实际位置</option></select></div>
      </div>
      <p className="mt-5 text-xs leading-6 text-secondary">周期只用于换算理论每秒周期数；下方播放速度固定，与真实网络延迟、总线利用率或设备能力无关。</p>
    </section>

    <dl className="grid gap-3 sm:grid-cols-3"><div className="panel p-4"><dt className="text-xs text-secondary">理论周期数 / 秒</dt><dd className="mt-2 font-mono text-2xl text-accent">{cyclesPerSecond(cycle).toLocaleString()}</dd></div><div className="panel p-4"><dt className="text-xs text-secondary">输出过程数据</dt><dd className="mt-2 font-mono text-2xl">{outputTotal} <span className="text-base text-secondary">Byte</span></dd></div><div className="panel p-4"><dt className="text-xs text-secondary">输入过程数据</dt><dd className="mt-2 font-mono text-2xl">{inputTotal} <span className="text-base text-secondary">Byte</span></dd></div></dl>

    <section aria-labelledby="journey-title" className="panel min-w-0 p-5 sm:p-6">
      <div className="flex flex-wrap items-start justify-between gap-4"><div><h2 id="journey-title" className="text-xl font-semibold">帧的旅程</h2><p className="mt-2 text-sm text-secondary">点击从站查看映射；用下一步或播放观察读取与写入。</p></div><span className="rounded-full border border-border px-3 py-1 text-xs text-secondary">步骤 {stage.current + 1} / {slaveCount + 2}</span></div>
      <div className="mt-6 overflow-x-auto pb-2"><div className="flex min-w-max items-center gap-2" aria-label="从主站经过从站再返回主站的示意路径"><div className={`w-28 rounded-lg border p-3 text-center text-sm ${stage.current === 0 ? "border-accent bg-accent-soft text-accent" : "border-border"}`}>主站<br /><span className="text-xs">输出准备</span></div><ArrowRight size={17} className="shrink-0 text-secondary" aria-hidden="true" />{layout.map((slave) => <div key={slave.number} className="flex items-center gap-2"><button type="button" aria-pressed={selectedSlave === slave.number} onClick={() => setSelectedSlave(slave.number)} className={`w-28 rounded-lg border p-3 text-left text-sm ${stage.activeSlave === slave.number ? "border-accent bg-accent-soft" : "border-border"} ${selectedSlave === slave.number ? "ring-2 ring-accent" : ""}`}><span className="block font-medium">从站 {slave.number}</span><span className="mt-1 block text-xs text-secondary">{slave.number <= stage.processedCount ? "已处理" : "待经过"}</span></button><ArrowRight size={17} className="shrink-0 text-secondary" aria-hidden="true" /></div>)}<div className={`w-28 rounded-lg border p-3 text-center text-sm ${stage.returned ? "border-success bg-accent-soft" : "border-border"}`}>主站<br /><span className="text-xs">输入返回</span></div></div></div>
      <p role="status" aria-live="polite" className="mt-4 min-h-12 rounded-lg bg-surface-hover px-4 py-3 text-sm leading-6">{stageText}</p>
      <div className="mt-4 flex flex-wrap gap-2"><button type="button" onClick={() => setPlaying((current) => !current)} className="atlas-learning-button inline-flex items-center gap-2">{playing ? <Pause size={15} aria-hidden="true" /> : <Play size={15} aria-hidden="true" />}{playing ? "暂停" : "播放"}</button><button type="button" onClick={() => { setPlaying(false); setStep((current) => current >= slaveCount + 1 ? 0 : current + 1); }} className="atlas-learning-button inline-flex items-center gap-2"><StepForward size={15} aria-hidden="true" />下一步</button><button type="button" onClick={restart} className="atlas-learning-button inline-flex items-center gap-2"><RotateCcw size={15} aria-hidden="true" />重置</button></div>
    </section>

    <section aria-labelledby="mapping-title" className="panel p-5 sm:p-6"><div className="flex flex-wrap items-baseline justify-between gap-2"><h2 id="mapping-title" className="text-xl font-semibold">从站 {selected.number} 的 PDO 映射</h2><p className="text-xs text-secondary">示例字段；字节偏移从各方向的过程映像起点计算</p></div><div className="mt-5 grid gap-5 md:grid-cols-2"><div className="rounded-lg border border-border p-4"><h3 className="font-medium">主站输出 → 从站接收 <span className="text-xs text-secondary">RxPDO</span></h3><FieldRows fields={mapping.output} offset={selected.outputOffset} /></div><div className="rounded-lg border border-border p-4"><h3 className="font-medium">从站发送 → 主站输入 <span className="text-xs text-secondary">TxPDO</span></h3><FieldRows fields={mapping.input} offset={selected.inputOffset} /></div></div><p className="mt-5 text-xs leading-6 text-secondary">此处将相同示例从站顺序拼接，并按整字节字段显示偏移。真实映射、打包、对齐、同步及帧开销必须以 ESI、设备手册和主站配置为准。</p></section>
  </div>;
}
