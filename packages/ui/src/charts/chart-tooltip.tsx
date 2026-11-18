interface TooltipEntry {
  dataKey?: unknown;
  value?: unknown;
  color?: string;
  stroke?: string;
  fill?: string;
}

export interface ChartTooltipProps {
  active?: boolean;
  payload?: ReadonlyArray<TooltipEntry>;
  label?: unknown;
  /** Nome legível de cada série, por dataKey. */
  seriesLabels: Record<string, string>;
  formatValue: (value: number) => string;
  formatLabel?: (label: string) => string;
  shape?: "line" | "rect";
}

/** Tooltip único com todas as séries: valor em destaque, nome secundário, chave em traço. */
export function ChartTooltip({ active, payload, label, seriesLabels, formatValue, formatLabel, shape = "line" }: ChartTooltipProps) {
  if (!active || !payload?.length) return null;

  const title = String(label ?? "");
  return (
    <div className="min-w-40 rounded-lg border border-border bg-surface-raised px-3 py-2.5 text-xs shadow-pop">
      <p className="mb-1.5 font-medium text-muted-foreground">{formatLabel ? formatLabel(title) : title}</p>
      <ul className="space-y-1">
        {payload.map((entry) => {
          const key = String(entry.dataKey);
          const color = entry.stroke ?? entry.fill ?? entry.color;
          return (
            <li key={key} className="flex items-center gap-2">
              <span
                aria-hidden
                className={shape === "line" ? "h-0.5 w-3 rounded-full" : "size-2 rounded-[2px]"}
                style={{ background: color }}
              />
              <span className="font-semibold text-foreground tabular-nums">
                {typeof entry.value === "number" ? formatValue(entry.value) : "—"}
              </span>
              <span className="text-foreground-secondary">{seriesLabels[key] ?? key}</span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
