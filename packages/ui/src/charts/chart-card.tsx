import { BarChart3, Table2 } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Card, CardHeader } from "../components/card";
import { Table, Td, Th, Tr } from "../components/table";
import { cn } from "../lib/cn";

export interface LegendItem {
  label: string;
  color: string;
  /** Formato da chave: linha para gráficos de linha, retângulo para barras. */
  shape?: "line" | "rect";
  muted?: boolean;
}

export interface ChartTable {
  columns: { label: string; align?: "left" | "right" }[];
  rows: (string | number)[][];
}

interface ChartCardProps {
  title: string;
  description?: ReactNode;
  legend?: LegendItem[];
  /** Visão em tabela — sempre disponível para quem não lê o gráfico pela cor. */
  table: ChartTable;
  children: ReactNode;
  className?: string;
  footer?: ReactNode;
}

export function ChartCard({ title, description, legend, table, children, className, footer }: ChartCardProps) {
  const [view, setView] = useState<"chart" | "table">("chart");

  return (
    <Card className={cn("flex flex-col", className)}>
      <CardHeader
        title={title}
        description={description}
        actions={
          <div role="tablist" aria-label="Visualização" className="inline-flex rounded-lg border border-border bg-surface-sunken p-0.5">
            <ViewTab active={view === "chart"} onClick={() => setView("chart")} label="Gráfico">
              <BarChart3 className="size-3.5" aria-hidden />
            </ViewTab>
            <ViewTab active={view === "table"} onClick={() => setView("table")} label="Tabela">
              <Table2 className="size-3.5" aria-hidden />
            </ViewTab>
          </div>
        }
      />

      {legend && legend.length > 1 && view === "chart" ? (
        <ul className="flex flex-wrap gap-x-4 gap-y-1 px-5 pt-3 text-xs text-foreground-secondary">
          {legend.map((item) => (
            <li key={item.label} className="inline-flex items-center gap-1.5">
              <span
                aria-hidden
                className={cn(item.shape === "line" ? "h-0.5 w-3.5 rounded-full" : "size-2.5 rounded-[3px]")}
                style={{ background: item.color }}
              />
              {item.label}
            </li>
          ))}
        </ul>
      ) : null}

      <div className="flex-1 px-2 pb-4 pt-3 sm:px-3">
        {view === "chart" ? (
          children
        ) : (
          <div className="-mx-2 sm:-mx-3">
            <Table>
              <thead>
                <tr>
                  {table.columns.map((c) => (
                    <Th key={c.label} align={c.align}>
                      {c.label}
                    </Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {table.rows.map((row, i) => (
                  <Tr key={i}>
                    {row.map((cell, j) => (
                      <Td key={j} align={table.columns[j]?.align}>
                        {cell}
                      </Td>
                    ))}
                  </Tr>
                ))}
              </tbody>
            </Table>
          </div>
        )}
      </div>
      {footer ? <div className="border-t border-border px-5 py-3 text-xs text-muted-foreground">{footer}</div> : null}
    </Card>
  );
}

function ViewTab({ active, onClick, label, children }: { active: boolean; onClick: () => void; label: string; children: ReactNode }) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground",
        active && "bg-surface-raised text-foreground shadow-card",
      )}
    >
      {children}
      {label}
    </button>
  );
}
