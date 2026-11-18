import { format } from "../lib/format";

export interface Segment {
  label: string;
  value: number;
  color: string;
}

/**
 * Barra segmentada fina (um todo dividido em partes). A legenda abaixo traz nome,
 * quantidade e percentual de cada parte — a cor nunca é o único canal.
 */
export function SegmentedBar({ segments, ariaLabel }: { segments: Segment[]; ariaLabel: string }) {
  const total = segments.reduce((sum, s) => sum + s.value, 0);
  const visible = segments.filter((s) => s.value > 0);

  return (
    <div>
      <div
        role="img"
        aria-label={`${ariaLabel}: ${segments.map((s) => `${s.label} ${s.value}`).join(", ")}`}
        className="flex h-2 w-full gap-0.5 overflow-hidden rounded-full bg-surface-sunken"
      >
        {visible.map((s) => (
          <div
            key={s.label}
            title={`${s.label}: ${format.integer(s.value)} (${format.percent(total ? s.value / total : 0)})`}
            className="h-full first:rounded-l-full last:rounded-r-full"
            style={{ width: `${(s.value / total) * 100}%`, background: s.color }}
          />
        ))}
      </div>
      <ul className="mt-4 space-y-2.5">
        {segments.map((s) => (
          <li key={s.label} className="flex items-center gap-2.5 text-sm">
            <span aria-hidden className="size-2.5 shrink-0 rounded-[3px]" style={{ background: s.color }} />
            <span className="flex-1 truncate text-foreground-secondary">{s.label}</span>
            <span className="font-semibold text-foreground tabular-nums">{format.integer(s.value)}</span>
            <span className="w-10 text-right text-xs text-muted-foreground tabular-nums">
              {format.percent(total ? s.value / total : 0)}
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
