import { Bar, BarChart, CartesianGrid, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ChartTooltip } from "./chart-tooltip";

export interface BarListItem {
  label: string;
  value: number;
}

interface BarListProps {
  data: BarListItem[];
  /** Nome da medida, usado no tooltip. */
  seriesLabel: string;
  color?: string;
  formatValue?: (value: number) => string;
  labelWidth?: number;
}

/** Barras horizontais de uma série: ≤ 20px, ponta arredondada, valor na ponta. */
export function BarList({ data, seriesLabel, color = "var(--series-1)", formatValue = String, labelWidth = 128 }: BarListProps) {
  const height = data.length * 36 + 24;

  return (
    <div style={{ height }} className="w-full">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: 48, bottom: 4, left: 4 }} barCategoryGap={8}>
          <CartesianGrid horizontal={false} stroke="var(--chart-grid)" />
          <XAxis type="number" hide domain={[0, "dataMax"]} />
          <YAxis
            type="category"
            dataKey="label"
            width={labelWidth}
            tickLine={false}
            axisLine={{ stroke: "var(--chart-axis)" }}
            tick={{ fill: "var(--foreground-secondary)", fontSize: 12 }}
          />
          <Tooltip
            cursor={{ fill: "var(--surface-sunken)" }}
            content={(props) => (
              <ChartTooltip
                active={props.active}
                payload={props.payload}
                label={props.label}
                seriesLabels={{ value: seriesLabel }}
                formatValue={formatValue}
                shape="rect"
              />
            )}
          />
          <Bar dataKey="value" fill={color} barSize={18} radius={[0, 4, 4, 0]} isAnimationActive={false}>
            <LabelList
              dataKey="value"
              position="right"
              offset={8}
              formatter={(v: unknown) => (typeof v === "number" ? formatValue(v) : "")}
              style={{ fill: "var(--foreground)", fontSize: 12, fontWeight: 600 }}
            />
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
