import { ArrowDownRight, ArrowUpRight, Minus, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "../lib/cn";
import { Card } from "./card";

export interface StatDelta {
  /** Variação já formatada, ex.: "+12%" ou "−3". */
  label: string;
  direction: "up" | "down" | "flat";
  /** Se subir é bom, ruim ou neutro (neutro = sem cor de estado). */
  upIs: "good" | "bad" | "neutral";
  /** Período de comparação, ex.: "vs 2º bimestre". */
  comparedTo: string;
}

export interface StatTileProps {
  label: string;
  value: ReactNode;
  icon?: LucideIcon;
  delta?: StatDelta;
  hint?: ReactNode;
  className?: string;
}

export function StatTile({ label, value, icon: Icon, delta, hint, className }: StatTileProps) {
  return (
    <Card className={cn("flex flex-col gap-3 p-5", className)}>
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm font-medium text-foreground-secondary">{label}</p>
        {Icon ? (
          <span className="grid size-8 place-items-center rounded-lg bg-primary-soft text-primary-soft-foreground">
            <Icon className="size-4" aria-hidden />
          </span>
        ) : null}
      </div>
      <p className="text-[28px] font-semibold leading-none tracking-tight text-foreground">{value}</p>
      {delta || hint ? (
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs">
          {delta ? <DeltaPill delta={delta} /> : null}
          {delta ? <span className="text-muted-foreground">{delta.comparedTo}</span> : null}
          {hint ? <span className="text-muted-foreground">{hint}</span> : null}
        </div>
      ) : null}
    </Card>
  );
}

function DeltaPill({ delta }: { delta: StatDelta }) {
  const Icon = delta.direction === "up" ? ArrowUpRight : delta.direction === "down" ? ArrowDownRight : Minus;
  const sentiment =
    delta.direction === "flat" || delta.upIs === "neutral"
      ? "neutral"
      : (delta.direction === "up") === (delta.upIs === "good")
        ? "good"
        : "bad";

  return (
    <span
      className={cn(
        "inline-flex items-center gap-0.5 rounded-md px-1.5 py-0.5 font-medium",
        sentiment === "good" && "bg-good-soft text-good-text",
        sentiment === "bad" && "bg-critical-soft text-critical-text",
        sentiment === "neutral" && "bg-surface-sunken text-foreground-secondary",
      )}
    >
      <Icon className="size-3.5" aria-hidden />
      {delta.label}
    </span>
  );
}
