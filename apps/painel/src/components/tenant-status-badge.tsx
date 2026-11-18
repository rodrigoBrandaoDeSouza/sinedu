import { Badge } from "@sinedu/ui";
import type { TenantStatus } from "../data/overview";

const config: Record<TenantStatus, { label: string; tone: "good" | "info" | "critical" | "warning" | "neutral" }> = {
  ativa: { label: "Ativa", tone: "good" },
  provisionando: { label: "Provisionando", tone: "info" },
  suspensa: { label: "Suspensa", tone: "critical" },
  falha_provisionamento: { label: "Falha no provisionamento", tone: "critical" },
  encerrando: { label: "Encerrando", tone: "warning" },
};

export function TenantStatusBadge({ status }: { status: TenantStatus }) {
  const { label, tone } = config[status];
  return (
    <Badge tone={tone} icon>
      {label}
    </Badge>
  );
}
