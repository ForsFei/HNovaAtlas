/** Return the final heading whose top has passed the reading threshold. */
export function lastHeadingAtOrBefore(count: number, topAt: (index: number) => number, threshold: number): number {
  let first = 0;
  let end = count;
  while (first < end) {
    const middle = first + Math.floor((end - first) / 2);
    if (topAt(middle) <= threshold) first = middle + 1;
    else end = middle;
  }
  return first - 1;
}
