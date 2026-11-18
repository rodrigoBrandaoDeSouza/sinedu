import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ChartTooltip } from "./chart-tooltip";

export interface ColumnSeries {
  key: string;
  label: string;
  color: string;
}

interface ColumnGroupsProps {
  data: Record<string, string | number>[];
  xKey: string;
  series: ColumnSeries[];
  height?: number;
  formatValue?: (value: number) => string;
  formatAxis?: (value: number) => string;
}

/** Colunas agrupadas (até 3 séries), 2px de respiro entre barras vizinhas, um único eixo. */
export function ColumnGroups({ data, xKey, series, height = 260, formatValue = String, formatAxis }: ColumnGroupsProps) {
  const labels = Object.fromEntries(series.map((s) => [s.key, s.label]));

  return (
    <div style={{ height }} className="w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 16, right: 8, bottom: 0, left: 0 }} barGap={2} barCategoryGap="28%">
          <CartesianGrid vertical={false} stroke="var(--chart-grid)" />
          <XAxis
            dataKey={xKey}
            tickLine={false}
            axisLine={{ stroke: "var(--chart-axis)" }}
            tick={{ fill: "var(--chart-muted)", fontSize: 12 }}
            tickMargin={8}
          />
          <YAxis
            tickLine={false}
            axisLine={false}
            tick={{ fill: "var(--chart-muted)", fontSize: 12 }}
            tickFormatter={(v: number) => (formatAxis ?? formatValue)(v)}
            width={76}
          />
          <Tooltip
            cursor={{ fill: "var(--surface-sunken)" }}
            content={(props) => (
              <ChartTooltip
                active={props.active}
                payload={props.payload}
                label={props.label}
                seriesLabels={labels}
                formatValue={formatValue}
                shape="rect"
              />
            )}
          />
          {series.map((s) => (
            <Bar key={s.key} dataKey={s.key} fill={s.color} maxBarSize={20} radius={[4, 4, 0, 0]} isAnimationActive={false} />
          ))}
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
