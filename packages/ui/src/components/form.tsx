import { AlertTriangle, CheckCircle2, Info, XCircle } from "lucide-react";
import { forwardRef, useId, type InputHTMLAttributes, type ReactNode, type TextareaHTMLAttributes } from "react";
import { cn } from "../lib/cn";

/**
 * Layout comum dos campos: rótulo, controle e ajuda/erro em três linhas.
 * Dentro de um grid (ex.: "grid sm:grid-cols-2"), o campo vira subgrid e ocupa três linhas do pai,
 * então rótulos que quebram em duas linhas não desalinham os controles vizinhos.
 * Fora de um grid, comporta-se como uma coluna simples.
 */
export const fieldLayout = "row-span-3 grid grid-rows-subgrid content-start gap-y-1.5";
export const fieldLabel = "self-end text-sm font-medium text-foreground";
export const fieldControl =
  "w-full rounded-lg border border-border-strong bg-surface-raised text-sm text-foreground transition-colors placeholder:text-muted-foreground hover:border-foreground/30 disabled:cursor-not-allowed disabled:opacity-60";


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
    <div className={cn(fieldLayout, className)}>
      <label htmlFor={inputId} className={fieldLabel}>
        {label}
      </label>
      <input
        ref={ref}
        id={inputId}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        className={cn(
          fieldControl,
          "h-10 px-3",
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

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label: string;
  error?: string;
  hint?: ReactNode;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea({ label, error, hint, className, id, rows = 3, style, ...props }, ref) {
  const generated = useId();
  const inputId = id ?? generated;
  const describedBy = error ? `${inputId}-error` : hint ? `${inputId}-hint` : undefined;

  return (
    <div className={cn(fieldLayout, className)}>
      <label htmlFor={inputId} className={fieldLabel}>
        {label}
      </label>
      <textarea
        ref={ref}
        id={inputId}
        rows={rows}
        // Em grid, a altura natural do <textarea> pode ser comprimida a zero: a altura mínima garante as linhas pedidas
        // (linha de 1.25rem do text-sm + padding vertical + borda).
        style={{ minHeight: `calc(${rows} * 1.25rem + 1rem + 2px)`, ...style }}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        className={cn(
          fieldControl,
          "px-3 py-2",
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
