import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { Card } from "./card";

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
}: {
  icon: LucideIcon;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
}) {
  return (
    <Card className="flex flex-col items-center justify-center px-6 py-16 text-center">
      <span className="mb-4 grid size-12 place-items-center rounded-2xl bg-primary-soft text-primary-soft-foreground">
        <Icon className="size-6" aria-hidden />
      </span>
      <h2 className="text-base font-semibold text-foreground">{title}</h2>
      {description ? <p className="mt-1 max-w-md text-sm text-muted-foreground">{description}</p> : null}
      {action ? <div className="mt-5">{action}</div> : null}
    </Card>
  );
}
