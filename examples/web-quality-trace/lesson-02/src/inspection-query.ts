import type { operations } from "./generated-query";

type InspectionQuery = NonNullable<operations["ListInspections"]["parameters"]["query"]>;

type CursorQuery = operations["ListInspectionCursorPage"]["parameters"]["query"];

function queryUrl(path: string, query: InspectionQuery | CursorQuery) {
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value !== undefined && value !== "") params.set(name, String(value));
  }
  return params.size ? `${path}?${params}` : path;
}

export function inspectionUrl(query: InspectionQuery) {
  return queryUrl("/api/inspections", query);
}

export function inspectionCursorUrl(query: CursorQuery) {
  return queryUrl("/api/inspection-pages", query);
}
