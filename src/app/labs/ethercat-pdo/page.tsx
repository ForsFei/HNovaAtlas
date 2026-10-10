import type { Metadata } from "next";
import Link from "next/link";
import { PdoLab } from "@/components/labs/PdoLab";

export const metadata: Metadata = { title: "EtherCAT PDO 交互实验", description: "逐步观察 EtherCAT PDO 数据方向、示例映射与主站过程映像。" };

export default function EthercatPdoLabPage() {
  return <div className="container-shell py-10 sm:py-14"><p className="eyebrow mb-3">Interactive lab / EtherCAT</p><h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">PDO 过程数据实验</h1><p className="mt-4 max-w-3xl text-base leading-8 text-secondary">让示意帧依次经过从站，观察主站输出如何被读取、从站输入如何写回，并对照 PDO 映射与过程映像的字节偏移。</p><div className="my-7 rounded-lg border border-warning px-4 py-3 text-sm leading-7 text-secondary"><strong className="text-primary">教学模型：</strong>它不生成真实 EtherCAT 报文，不连接硬件，也不估算物理延迟、吞吐量或可达的控制周期。</div><PdoLab /><section className="mt-10 border-t border-border pt-7"><h2 className="text-lg font-semibold">配套阅读</h2><div className="mt-4 flex flex-wrap gap-4 text-sm text-accent"><Link href="/knowledge/industrial/ethercat/communication/pdo/">PDO 概览 →</Link><Link href="/knowledge/industrial/ethercat/communication/pdo-mapping/">PDO Mapping →</Link><Link href="/knowledge/industrial/ethercat/communication/process-image/">过程映像 →</Link></div></section></div>;
}
