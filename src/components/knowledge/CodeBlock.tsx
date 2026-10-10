import type { HTMLAttributes } from "react";
import { CopyButton } from "./CopyButton";

type Props = HTMLAttributes<HTMLPreElement> & { "data-code"?: string; "data-filename"?: string; "data-language"?: string };

export function CodeBlock({ "data-code": code, "data-filename": filename, "data-language": language, ...props }: Props) {
  return <div className="atlas-code">
    <div className="atlas-code-toolbar"><span className="min-w-0 truncate" title={filename}>{filename ?? language ?? "代码"}</span>{code !== undefined && <CopyButton code={code} />}</div>
    <pre {...props} tabIndex={0} aria-label={filename ? `代码：${filename}` : "代码块"} />
  </div>;
}
