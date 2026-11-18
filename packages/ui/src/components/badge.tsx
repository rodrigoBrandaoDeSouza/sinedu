import { cva, type VariantProps } from "class-variance-authority";
import { AlertTriangle, CheckCircle2, Clock3, Info, XCircle, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { cn } from "../lib/cn";

const badgeVariants = cva(
  "inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium [&_svg]:size-3.5",
  {
    variants: {
      tone: {
        neutral: "bg-surface-sunken text-foreground-secondary",
        brand: "bg-primary-soft text-primary-soft-foreground",
        info: "bg-info-soft text-info-text",
        good: "bg-good-soft text-good-text",
        warning: "bg-warning-soft text-warning-text",
        critical: "bg-critical-soft text-critical-text",
        outline: "border border-border-strong text-foreground-secondary",
      },
    },
    defaultVariants: { tone: "neutral" },
  },
);

/** Estados sempre trazem ícone + texto: a cor nunca carrega o significado sozinha. */
const toneIcons: Partial<Record<NonNullable<BadgeProps["tone"]>, LucideIcon>> = {
  good: CheckCircle2,
  warning: AlertTriangle,
  critical: XCircle,
  info: Info,
  neutral: Clock3,
};

export interface BadgeProps extends VariantProps<typeof badgeVariants> {
  children: ReactNode;
  icon?: boolean;
  className?: string;
}

export function Badge({ tone, icon = false, className, children }: BadgeProps) {
  const Icon = icon && tone ? toneIcons[tone] : undefined;
  return (
    <span className={cn(badgeVariants({ tone }), className)}>
      {Icon ? <Icon aria-hidden /> : null}
      {children}
    </span>
  );
}
