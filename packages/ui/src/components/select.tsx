import { ChevronDown } from "lucide-react";
import type { SelectHTMLAttributes } from "react";
import { cn } from "../lib/cn";

export interface SelectOption {
  value: string;
  label: string;
}

export interface SelectProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, "children"> {
  label: string;
  options: SelectOption[];
  /** Esconde o rótulo visualmente (continua acessível). */
  hideLabel?: boolean;
}

export function Select({ label, options, hideLabel = false, className, id, ...props }: SelectProps) {
  const selectId = id ?? `select-${label.toLowerCase().replace(/\W+/g, "-")}`;
  return (
    <label htmlFor={selectId} className={cn("flex flex-col gap-1", className)}>
      <span className={cn("text-xs font-medium text-muted-foreground", hideLabel && "sr-only")}>{label}</span>
      <span className="relative">
        <select
          id={selectId}
          className="h-9 w-full appearance-none rounded-lg border border-border-strong bg-surface-raised pl-3 pr-9 text-sm text-foreground transition-colors hover:border-foreground/30"
          {...props}
        >
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <ChevronDown
          className="pointer-events-none absolute right-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden
        />
      </span>
    </label>
  );
}
