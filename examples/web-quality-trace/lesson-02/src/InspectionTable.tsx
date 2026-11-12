import type { Inspection } from "./inspection-schema";

export function InspectionTable({ items, caption, onSelect }: {
  items: readonly Inspection[];
  caption: string;
  onSelect: (id: string) => void;
}) {
  return <table>
    <caption>{caption}</caption>
    <thead><tr><th scope="col">周期</th><th scope="col">工位</th><th scope="col">结果</th></tr></thead>
    <tbody>
      {items.map(row =>
        <tr key={row.id}>
          <td><button onClick={() => onSelect(row.id)}>{row.id}</button></td>
          <td>{row.station}</td>
          <td>{row.result}</td>
        </tr>
      )}
    </tbody>
  </table>;
}
