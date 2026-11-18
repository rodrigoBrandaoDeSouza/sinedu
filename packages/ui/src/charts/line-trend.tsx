import { Area, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ChartTooltip } from "./chart-tooltip";

export interface LineSeries {
  key: string;
  label: string;
  color: string;
  /** Série principal: ganha preenchimento suave e rótulo no último ponto. */
  primary?: boolean;
}

interface LineTrendProps {
  data: Record<string, string | number | null>[];
  xKey: string;
  series: LineSeries[];
  height?: number;
  formatValue?: (value: number) => string;
  formatX?: (value: string) => string;
}

/** Linha temporal: 2px, um único eixo, crosshair + tooltip com todas as séries. */
export function LineTrend({ data, xKey, series, height = 260, formatValue = String, formatX }: LineTrendProps) {
  const labels = Object.fromEntries(series.map((s) => [s.key, s.label]));
  // Rótulo vai no último ponto com valor da série principal (a série pode terminar antes do eixo).
  const primaryKey = series.find((s) => s.primary)?.key;
  const lastIndex = primaryKey ? data.findLastIndex((d) => typeof d[primaryKey] === "number") : -1;

  return (
    <div style={{ height }} className="w-full">
      <ResponsiveContainer width="100%" height="100%">
        <ComposedChart data={data} margin={{ top: 16, right: 56, bottom: 0, left: 0 }}>
          <defs>
            {series
              .filter((s) => s.primary)
              .map((s) => (
                <linearGradient key={s.key} id={`fill-${s.key}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={s.color} stopOpacity={0.14} />
                  <stop offset="100%" stopColor={s.color} stopOpacity={0.02} />
                </linearGradient>
              ))}
          </defs>
          <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
          <XAxis
            dataKey={xKey}
            tickLine={false}
            axisLine={{ stroke: "var(--chart-axis)" }}
            tick={{ fill: "var(--chart-muted)", fontSize: 12 }}
            tickFormatter={formatX}
            tickMargin={8}
            minTickGap={16}
          />
          <YAxis
            tickLine={false}
            axisLine={false}
            tick={{ fill: "var(--chart-muted)", fontSize: 12 }}
            tickFormatter={(v: number) => formatValue(v)}
            width={40}
            allowDecimals={false}
          />
          <Tooltip
            cursor={{ stroke: "var(--chart-axis)", strokeWidth: 1 }}
            content={(props) => (
              <ChartTooltip
                active={props.active}
                payload={props.payload?.filter((p) => p.type !== "none")}
                label={props.label}
                seriesLabels={labels}
                formatValue={formatValue}
                formatLabel={formatX}
              />
            )}
          />
          {series
            .filter((s) => s.primary)
            .map((s) => (
              <Area
                key={`area-${s.key}`}
                dataKey={s.key}
                type="monotone"
                stroke="none"
                fill={`url(#fill-${s.key})`}
                tooltipType="none"
                isAnimationActive={false}
              />
            ))}
          {series.map((s) => (
            <Line
              key={s.key}
              dataKey={s.key}
              type="monotone"
              stroke={s.color}
              strokeWidth={2}
              strokeLinecap="round"
              strokeLinejoin="round"
              dot={false}
              activeDot={{ r: 4.5, fill: s.color, stroke: "var(--surface)", strokeWidth: 2 }}
              connectNulls={false}
              isAnimationActive={false}
              label={
                s.primary
                  ? (props: { index?: number; x?: number | string; y?: number | string; value?: unknown }) =>
                      props.index === lastIndex && typeof props.value === "number" ? (
                        <g>
                          <circle cx={Number(props.x)} cy={Number(props.y)} r={4.5} fill={s.color} stroke="var(--surface)" strokeWidth={2} />
                          <text x={Number(props.x) + 10} y={Number(props.y)} dy={4} fontSize={12} fontWeight={600} fill="var(--foreground)">
                            {formatValue(props.value)}
                          </text>
                        </g>
                      ) : (
                        <g />
                      )
                  : undefined
              }
            />
          ))}
        </ComposedChart>
      </ResponsiveContainer>
    </div>
  );
}
