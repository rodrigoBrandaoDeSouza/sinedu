import { useQuery } from "@tanstack/react-query";
import { Badge, Card, EmptyState, PageHeader, Table, Td, Th, Tr } from "@sinedu/ui";
import { CalendarClock } from "lucide-react";
import { Link } from "react-router";
import { formatDate } from "../../lib/school-api";
import { protectedApi, protectionKindLabels, type PendingItem } from "../../lib/protected-api";
import { ProtectedState } from "./shared";

const typeInfo: Record<PendingItem["type"], { label: string; tone: "warning" | "critical" | "info" }> = {
  health_review: { label: "Revisar ficha de saúde", tone: "info" },
  protection_expiring: { label: "Medida vence em breve", tone: "warning" },
  protection_expired: { label: "Medida vencida", tone: "critical" },
};

/** F4-04: revisões e vigências nos próximos 30 dias ou vencidas. Sem conteúdo, só o que precisa de atenção. */
export default function PendingPage() {
  const pending = useQuery({ queryKey: ["protected", "pending"], queryFn: protectedApi.pending, retry: false });

  return (
    <>
      <PageHeader
        title="Pendências sigilosas"
        description="Fichas de saúde com revisão marcada e medidas de proteção que vencem em até 30 dias ou já venceram."
      />
      <ProtectedState error={pending.error}>
        {pending.data?.length === 0 ? (
          <EmptyState icon={CalendarClock} title="Nada pendente" description="Nenhuma revisão ou vigência nos próximos 30 dias." />
        ) : (
          <Card>
            <Table>
              <thead>
                <tr>
                  <Th>Aluno</Th>
                  <Th>Pendência</Th>
                  <Th>Data</Th>
                </tr>
              </thead>
              <tbody>
                {pending.data?.map((p) => (
                  <Tr key={`${p.type}-${p.recordId}`}>
                    <Td>
                      <Link className="font-medium text-foreground hover:underline" to={`/alunos/${p.studentId}?aba=${p.type === "health_review" ? "saude" : "protecao"}`}>
                        {p.studentName}
                      </Link>
                      {p.classroom ? <p className="text-xs text-muted-foreground">{p.classroom}</p> : null}
                    </Td>
                    <Td>
                      <Badge tone={typeInfo[p.type].tone} icon>
                        {typeInfo[p.type].label}
                      </Badge>
                      {p.kind ? <p className="mt-1 text-xs text-muted-foreground">{protectionKindLabels[p.kind]}</p> : null}
                    </Td>
                    <Td className="tabular-nums text-foreground-secondary">{formatDate(p.dueOn)}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          </Card>
        )}
      </ProtectedState>
    </>
  );
}
