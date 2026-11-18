import { AlertTriangle, CheckCircle2, Info, XCircle } from "lucide-react";
import { forwardRef, useId, type InputHTMLAttributes, type ReactNode } from "react";
import { cn } from "../lib/cn";

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  error?: string;
  hint?: ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input({ label, error, hint, className, id, ...props }, ref) {
  const generated = useId();
  const inputId = id ?? generated;
  const describedBy = error ? `${inputId}-error` : hint ? `${inputId}-hint` : undefined;

  return (
    <div className={cn("flex flex-col gap-1.5", className)}>
      <label htmlFor={inputId} className="text-sm font-medium text-foreground">
        {label}
      </label>
      <input
        ref={ref}
        id={inputId}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        className={cn(
          "h-10 w-full rounded-lg border border-border-strong bg-surface-raised px-3 text-sm text-foreground transition-colors placeholder:text-muted-foreground hover:border-foreground/30",
          error && "border-critical",
        )}
        {...props}
      />
      {error ? (
        <p id={`${inputId}-error`} className="text-xs text-critical-text">
          {error}
        </p>
      ) : hint ? (
        <p id={`${inputId}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
});

const alertStyles = {
  info: { className: "border-info-text/20 bg-info-soft text-info-text", icon: Info },
  good: { className: "border-good-text/20 bg-good-soft text-good-text", icon: CheckCircle2 },
  warning: { className: "border-warning-text/20 bg-warning-soft text-warning-text", icon: AlertTriangle },
  critical: { className: "border-critical-text/20 bg-critical-soft text-critical-text", icon: XCircle },
} as const;

export function Alert({ tone = "info", title, children, className }: { tone?: keyof typeof alertStyles; title?: string; children?: ReactNode; className?: string }) {
  const { className: toneClass, icon: Icon } = alertStyles[tone];
  return (
    <div role={tone === "critical" ? "alert" : "status"} className={cn("flex gap-2.5 rounded-lg border px-3 py-2.5 text-sm", toneClass, className)}>
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
      <div className="min-w-0">
        {title ? <p className="font-medium">{title}</p> : null}
        {children ? <div className={cn(title && "mt-0.5", "text-foreground-secondary")}>{children}</div> : null}
      </div>
    </div>
  );
}
