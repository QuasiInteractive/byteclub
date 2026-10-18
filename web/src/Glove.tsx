// An original pixel-art pointing glove, drawn from a character grid.
// K outline, W white, L light shade, G mid shade, D dark shade, "." transparent.
const ROWS = [
  "....KKKKKK..........",
  "...KWWWWWWKKKKKKKKK.",
  "..KWWWWWWWWWWWWWWWWK",
  ".KWWWLWWWWWWWWWWWLLK",
  ".KWWLLWWWWWKKKKKKKK.",
  "KWWWWWWWWWWLK.......",
  "KWLWWWWWWWLGK.......",
  "KWLLWWWWWWLGK.......",
  ".KLWWWWWWLGK........",
  "..KGLLLLGGDK........",
  "...KKKKKKKK.........",
];
const COLOURS: Record<string, string> = { K: "#0a0a0a", W: "#ffffff", L: "#d9dbe2", G: "#a7abb8", D: "#6d7280" };

export function Glove({ size = 2, className }: { size?: number; className?: string }) {
  const w = ROWS[0].length;
  const h = ROWS.length;
  return (
    <svg className={className} width={w * size} height={h * size} viewBox={`0 0 ${w} ${h}`}
         shapeRendering="crispEdges" aria-hidden="true">
      {ROWS.flatMap((row, y) =>
        [...row].map((c, x) =>
          c === "." ? null : <rect key={`${x}-${y}`} x={x} y={y} width={1} height={1} fill={COLOURS[c]} />))}
    </svg>
  );
}
