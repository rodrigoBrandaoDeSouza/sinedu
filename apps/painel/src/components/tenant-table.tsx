import { Button, format, Table, Td, Th, Tr } from "@sinedu/ui";
import { ExternalLink, MoreHorizontal } from "lucide-react";
import type { TenantRow } from "../data/overview";
import { TenantStatusBadge } from "./tenant-status-badge";

const BASE_DOMAIN = import.meta.env.VITE_BASE_DOMAIN ?? "sinedu.com.br";

export function TenantTable({ tenants, compact = false }: { tenants: TenantRow[]; compact?: boolean }) {
  return (
    <Table>
      <thead>
        <tr>
          <Th>Escola</Th>
          <Th>Situação</Th>
          {!compact ? <Th>Rede</Th> : null}
          <Th align="right">Alunos ativos</Th>
          {!compact ? <Th align="right">Usuários</Th> : null}
          {!compact ? <Th align="right">Armazenamento</Th> : null}
          <Th>Versão</Th>
          {!compact ? <Th>Contrato até</Th> : null}
          <Th>
            <span className="sr-only">Ações</span>
          </Th>
        </tr>
      </thead>
      <tbody>
        {tenants.map((t) => (
          <Tr key={t.id}>
            <Td>
              <div className="flex items-center gap-3">
                <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-primary-soft text-[11px] font-bold text-primary-soft-foreground">
                  {t.name
                    .split(" ")
                    .filter((w) => w.length > 2)
                    .slice(0, 2)
                    .map((w) => w[0])
                    .join("")}
                </span>
                <div className="min-w-0">
                  <p className="truncate font-medium">{t.name}</p>
                  <a
                    href={`https://${t.slug}.${BASE_DOMAIN}`}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-primary"
                  >
                    {t.slug}.{BASE_DOMAIN}
                    <ExternalLink className="size-3" aria-hidden />
                  </a>
                </div>
              </div>
            </Td>
            <Td>
              <TenantStatusBadge status={t.status} />
            </Td>
            {!compact ? <Td className="text-foreground-secondary">{t.network}</Td> : null}
            <Td align="right">{format.integer(t.activeStudents)}</Td>
            {!compact ? <Td align="right">{format.integer(t.activeUsers)}</Td> : null}
            {!compact ? <Td align="right">{format.decimal(t.storageGb)} GB</Td> : null}
            <Td className="font-mono text-xs text-foreground-secondary">{t.schemaVersion}</Td>
            {!compact ? (
              <Td className="text-foreground-secondary tabular-nums">
                {t.contractEnds ? new Date(`${t.contractEnds}T12:00:00Z`).toLocaleDateString("pt-BR") : "—"}
              </Td>
            ) : null}
            <Td align="right">
              <Button variant="ghost" size="icon-sm" aria-label={`Ações para ${t.name}`}>
                <MoreHorizontal />
              </Button>
            </Td>
          </Tr>
        ))}
      </tbody>
    </Table>
  );
}
