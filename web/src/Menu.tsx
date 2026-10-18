import { useEffect, useRef, useState, type ReactNode } from "react";
import { Glove } from "./Glove";
import { sfx } from "./sound";

export type MenuItem = { key: string; label: ReactNode; hint?: string; disabled?: boolean; aside?: ReactNode };

type Props = {
  items: MenuItem[];
  onSelect: (key: string) => void;
  onBack?: () => void;
  onFocusChange?: (key: string) => void;
  columns?: number;
  initial?: string;
  label: string;
  /** Right arrow in a single-column menu, e.g. to jump to a neighbouring menu. */
  onRight?: () => void;
  /** Take keyboard focus when shown. Off for a second menu on the same screen. */
  autoFocus?: boolean;
};

// FF-style menu: the glove points at the focused option. Arrows move it (with a tick), Enter or
// a click confirms (chime), Escape goes back. Each option is a real <button> for keyboards and
// screen readers.
export function Menu({ items, onSelect, onBack, onFocusChange, onRight, autoFocus = true, columns = 1, initial, label }: Props) {
  const [focus, setFocus] = useState(() => Math.max(0, items.findIndex((i) => i.key === initial)));
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const first = useRef(true);

  useEffect(() => {
    // first render: only grab focus if asked; later moves always follow the cursor
    if (first.current) { first.current = false; if (autoFocus) refs.current[focus]?.focus(); return; }
    refs.current[focus]?.focus();
  }, [focus]); // eslint-disable-line
  useEffect(() => { if (items[focus]) onFocusChange?.(items[focus].key); }, [focus]); // eslint-disable-line

  const move = (to: number) => {
    const next = (to + items.length) % items.length;
    if (next !== focus) { sfx.move(); setFocus(next); }
  };

  const choose = (i: number) => {
    const item = items[i];
    if (!item || item.disabled) { sfx.error(); return; }
    sfx.confirm();
    onSelect(item.key);
  };

  const onKey = (e: React.KeyboardEvent) => {
    const steps: Record<string, number> = columns > 1
      ? { ArrowDown: columns, ArrowUp: -columns, ArrowRight: 1, ArrowLeft: -1 }
      : { ArrowDown: 1, ArrowUp: -1 };
    const step = steps[e.key];
    if (step !== undefined) { e.preventDefault(); move(focus + step); }
    else if (e.key === "ArrowRight" && columns === 1 && onRight) { e.preventDefault(); sfx.move(); onRight(); }
    else if (e.key === "Escape" && onBack) { e.preventDefault(); sfx.back(); onBack(); }
  };

  return (
    <div role="menu" aria-label={label} className="menu" onKeyDown={onKey}
         style={{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}>
      {items.map((item, i) => (
        <button key={item.key} ref={(el) => { refs.current[i] = el; }} role="menuitem" type="button"
                className={`menu-item${i === focus ? " is-focus" : ""}${item.disabled ? " is-disabled" : ""}`}
                aria-disabled={item.disabled || undefined} tabIndex={i === focus ? 0 : -1}
                onMouseEnter={() => move(i)} onFocus={() => i !== focus && setFocus(i)} onClick={() => choose(i)}>
          {i === focus && <Glove className="glove" />}
          <span className="menu-label">{item.label}</span>
          {item.aside && <span className="menu-aside">{item.aside}</span>}
        </button>
      ))}
    </div>
  );
}
