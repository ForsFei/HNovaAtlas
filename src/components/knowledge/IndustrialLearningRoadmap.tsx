"use client";

import { useRef } from "react";
import { Maximize2, X } from "lucide-react";
import { slug } from "github-slugger";

const chapterPath = "/knowledge/industrial/host-computer/chapters/";

type RoadmapStep = { number: number; title: string; heading: string; summary: string; y: number };
type RoadmapBranch = { name: string; tone: string; side: "left" | "right"; y: number; steps: RoadmapStep[] };

const branches: RoadmapBranch[] = [
  { name: "一、工业通信", tone: "communication", side: "left", y: 245, steps: [
    { number: 1, title: "设备角色与数据契约", heading: "设备角色与通信数据契约", summary: "点位 · 类型 · 单位 · 状态语义", y: 90 },
    { number: 2, title: "串口与自定义报文", heading: "串口与自定义报文", summary: "RS-232/485 · 分帧 · 校验", y: 162 },
    { number: 3, title: "TCP / UDP", heading: "TCP/UDP 与连接生命周期", summary: "连接 · 响应匹配 · 超时与重连", y: 234 },
    { number: 4, title: "Modbus RTU / TCP", heading: "Modbus RTU/TCP", summary: "寄存器 · 数据解析 · 轮询读写", y: 306 },
    { number: 5, title: "PLC 通信与 SDK", heading: "PLC 通信与厂商 SDK", summary: "S7 · MC · OPC UA · 命令握手", y: 378 },
  ] },
  { name: "二、雷赛运动控制", tone: "motion", side: "left", y: 650, steps: [
    { number: 6, title: "运动基础与雷赛单轴", heading: "运动基础与雷赛单轴", summary: "E3032 主线 · 脉冲对照 · 单轴", y: 610 },
    { number: 7, title: "雷赛多轴与运动扩展", heading: "雷赛多轴与运动扩展", summary: "插补与 IO · PLC · 机械臂集成", y: 730 },
  ] },
  { name: "三、机器视觉", tone: "vision", side: "right", y: 245, steps: [
    { number: 8, title: "视觉基础与图像采集", heading: "视觉基础与图像采集", summary: "成像 · 坐标与标定 · 触发采集", y: 100 },
    { number: 9, title: "HALCON + C#", heading: "HALCON 与 C# 集成", summary: "HDevelop · 定位 · 测量与集成", y: 190 },
    { number: 10, title: "OpenCvSharp", heading: "OpenCvSharp 图像处理", summary: "Mat · 预处理 · 轮廓与几何", y: 280 },
    { number: 11, title: "YOLO 的 C# 部署", heading: "YOLO 的 C# 部署", summary: "ONNX Runtime · 推理与结果还原", y: 370 },
  ] },
  { name: "四、综合工位实践", tone: "practice", side: "right", y: 650, steps: [
    { number: 12, title: "综合检测工位", heading: "综合检测工位与工程实践", summary: "通信 × 运动 × 视觉", y: 672 },
  ] },
];

function RoadmapDiagram() {
  return <svg className="industrial-roadmap-svg" viewBox="0 0 1200 820" role="group" aria-label="工业上位机十二步学习路线，分为工业通信、雷赛运动控制、机器视觉和综合工位实践">
    <title>工业上位机学习路线图</title>
    <desc>从中央的 C# 与 WPF 应用向四个方向展开。按阶段编号一到十二学习，点击阶段查看详细目录。</desc>
    {branches.map((branch) => {
      const left = branch.side === "left";
      const anchorX = left ? 350 : 850;
      const rootX = left ? 480 : 720;
      const rootY = branch.y < 400 ? 443 : 480;
      const leafX = left ? 290 : 910;
      return <g key={branch.tone} className="industrial-roadmap-branch" data-tone={branch.tone}>
        <path className="industrial-roadmap-trunk" d={`M ${rootX} ${rootY} C ${left ? 402 : 798} ${rootY}, ${left ? 438 : 762} ${branch.y}, ${anchorX} ${branch.y}`} />
        <text className="industrial-roadmap-branch-label" x={anchorX} y={branch.y - 22} textAnchor={left ? "start" : "end"}>{branch.name}</text>
        {branch.steps.map((step) => <g key={step.number}>
          <path className="industrial-roadmap-twig" d={`M ${anchorX} ${branch.y} C ${left ? 312 : 888} ${branch.y}, ${left ? 326 : 874} ${step.y}, ${leafX} ${step.y}`} />
          <a className="industrial-roadmap-step" href={`${chapterPath}#section-${slug(`第 ${step.number} 步：${step.heading}`)}`} aria-label={`第 ${step.number} 步：${step.heading}，查看章节范围`}>
            <title>{`第 ${step.number} 步：${step.heading} · ${step.number === 1 ? "已交付" : step.number === 2 ? "基础链路已交付，实机待验收" : step.number === 3 ? "基础链路已交付，本机验证" : step.number === 4 ? "基础已交付，RTU 实测待验收" : step.number === 5 ? "基础读写与握手已交付，实机待验收" : step.number === 6 ? "基础教学已交付：E3032 主线与脉冲对照，实机及现场扩展待验收" : step.number === 7 ? "通用主线已交付：多轴、PLC交接与六轴；原生及实机待验收" : step.number === 8 ? "通用主线已交付：成像/采集/处理/坐标/WPF；实机及人工UI待验收" : step.number === 9 ? "24.11基础教学主线已交付：对象/分割/定位/测量/引擎/WPF；原生和人工UI待验收" : step.number === 10 ? "基础主线已交付：处理/测量/WPF/有界分流；人工UI/质量/长期性能待验收" : step.number === 11 ? "CPU推理/解码NMS已交付：新增66项手写+12项实际CPU；一般图片/质量/现场待交付" : "计划中"}`}</title>
            <rect className="industrial-roadmap-hit-area" x={left ? 38 : 904} y={step.y - 53} width="258" height={step.number === 12 ? 135 : 58} rx="8" />
            <text className="industrial-roadmap-step-label" x={left ? 52 : 916} y={step.y - 27}>
              <tspan className="industrial-roadmap-step-number">{String(step.number).padStart(2, "0")}</tspan>
              <tspan dx="10">{step.title}</tspan>
            </text>
            <text className="industrial-roadmap-step-summary" x={left ? 52 : 916} y={step.y - 7}>{step.summary}</text>
            <path className="industrial-roadmap-leaf-line" d={left ? `M 48 ${step.y} H ${leafX}` : `M ${leafX} ${step.y} H 1158`} />
            {step.number === 12 && <g className="industrial-roadmap-practice-notes">
              <text x="916" y={step.y + 28}>状态机 · 配方 · 报警与追溯</text>
              <text x="916" y={step.y + 54}>仿真测试 · 故障恢复 · 部署</text>
            </g>}
          </a>
        </g>)}
      </g>;
    })}
    <g className="industrial-roadmap-root">
      <rect x="480" y="408" width="240" height="106" rx="30" />
      <text x="600" y="443" textAnchor="middle" className="industrial-roadmap-root-title">工业上位机</text>
      <text x="600" y="470" textAnchor="middle" className="industrial-roadmap-root-title">学习路线</text>
      <text x="600" y="496" textAnchor="middle" className="industrial-roadmap-root-subtitle">C# · WPF · 12 步实践</text>
    </g>
    <text className="industrial-roadmap-foundation" x="600" y="553" textAnchor="middle">前置基础：C# / .NET 与 WPF</text>
    <text className="industrial-roadmap-caption" x="600" y="792" textAnchor="middle">先通信，再运动与视觉，最后完成设备联动</text>
  </svg>;
}

export function IndustrialLearningRoadmap() {
  const dialog = useRef<HTMLDialogElement>(null);

  return <>
    <figure className="industrial-roadmap">
      <figcaption className="industrial-roadmap-toolbar">
        <span><strong>工业上位机 · 12 步</strong><small>点击阶段查看章节范围</small></span>
        <button type="button" onClick={() => dialog.current?.showModal()} aria-haspopup="dialog"><Maximize2 size={15} aria-hidden="true" />查看大图</button>
      </figcaption>
      <div className="industrial-roadmap-canvas" tabIndex={0} aria-label="学习路线图，窄屏可左右滚动"><RoadmapDiagram /></div>
      <div className="industrial-roadmap-legend"><span>数字为建议学习顺序</span><span>专题内容按阶段建设中</span></div>
    </figure>
    <dialog ref={dialog} className="industrial-roadmap-dialog" aria-label="工业上位机学习路线大图" data-pagefind-ignore onClick={(event) => { if (event.target === event.currentTarget) dialog.current?.close(); }}>
      <div className="industrial-roadmap-dialog-panel">
        <div className="industrial-roadmap-toolbar">
          <span><strong>工业上位机学习路线</strong><small>点击阶段进入目录 · 窄屏可左右滚动</small></span>
          <button type="button" onClick={() => dialog.current?.close()} autoFocus><X size={16} aria-hidden="true" />关闭</button>
        </div>
        <div className="industrial-roadmap-canvas industrial-roadmap-canvas-expanded" tabIndex={0} aria-label="完整学习路线图，可滚动查看"><RoadmapDiagram /></div>
      </div>
    </dialog>
  </>;
}
