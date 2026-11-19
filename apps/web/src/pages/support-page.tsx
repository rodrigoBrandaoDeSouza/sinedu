import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardBody, CardHeader, EmptyState, PageHeader, Table, Td, Th, Tr } from "@sinedu/ui";
import { Check, Headset, ShieldOff, X } from "lucide-react";
import { useSession } from "../lib/session";

type GrantStatus = "Pending" | "Approved" | "Denied" | "Revoked" | "Cancelled" | "Expired";

interface SupportGrant {
  id: string;
  requesterName: string;
  requesterEmail: string;
  reason: string;
  durationHours: number;
  status: GrantStatus;
  requestedAt: string;
  decidedAt: string | null;
  decidedByName: string | null;
  expiresAt: string | null;
  revokedAt: string | null;
  actions: number;
  lastActionAt: string | null;
}

const statusBadge: Record<GrantStatus, { label: string; tone: "warning" | "good" | "neutral" | "critical" | "info" }> = {
  Pending: { label: "Aguardando decisão", tone: "warning" },
  Approved: { label: "Liberado", tone: "good" },
  Denied: { label: "Recusado", tone: "neutral" },
  Revoked: { label: "Revogado", tone: "critical" },
  Cancelled: { label: "Retirado pelo suporte", tone: "neutral" },
  Expired: { label: "Encerrado (prazo)", tone: "info" },
};

const dateTime = (value: string | null) => (value ? new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" }) : "—");

/**
 * P-11: pedidos de acesso da equipe da plataforma. Só a Direção decide; Admin técnico acompanha o registro.
 * O acesso liberado é somente leitura de turmas e cadastro de alunos, por no máximo 24 h.
 */
export function SupportPage() {
  const me = useSession();
  const client = useQueryClient();
  const director = me.roles.includes("DIRETOR");
  const { data, isLoading } = useQuery({
    queryKey: ["support-grants"],
    queryFn: () => api.get<{ grants: SupportGrant[]; synced: boolean }>("/api/access/support"),
    refetchInterval: 60_000,
  });

  const act = useMutation({
    mutationFn: ({ id, action }: { id: string; action: "approve" | "deny" | "revoke" }) => api.post(`/api/access/support/${id}/${action}`),
    onSettled: () => client.invalidateQueries({ queryKey: ["support-grants"] }),
  });

  const grants = data?.grants ?? [];
  const pending = grants.filter((g) => g.status === "Pending");
  const active = grants.filter((g) => g.status === "Approved");
  const history = grants.filter((g) => g.status !== "Pending" && g.status !== "Approved");

  return (
    <>
      <PageHeader
        title="Acesso de suporte"
        description="A equipe da plataforma só entra na escola com liberação da Direção, por até 24 horas e somente para leitura."
      />

      <Alert tone="info" title="O que o suporte vê quando liberado" className="mb-4">
        Turmas e cadastro de alunos (com responsáveis e endereço), sem poder alterar nada. Ocorrências, comunicações, relatórios, Saúde e
        Proteção/Sigilo <strong>nunca</strong> ficam visíveis. Cada tela aberta fica registrada abaixo.
      </Alert>

      {data && !data.synced ? (
        <Alert tone="warning" title="Não foi possível buscar pedidos novos agora." className="mb-4">
          A lista mostra o que já estava registrado. Tente de novo em alguns minutos.
        </Alert>
      ) : null}
      {act.error ? (
        <Alert tone="critical" title={act.error instanceof ApiError ? act.error.message : "Não foi possível concluir."} className="mb-4" />
      ) : null}

      {isLoading ? (
        <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" />
      ) : grants.length === 0 ? (
        <EmptyState icon={Headset} title="Nenhum pedido de suporte" description="Quando a equipe da plataforma pedir acesso, o pedido aparece aqui e os diretores recebem um e-mail." />
      ) : (
        <div className="space-y-4">
          {pending.map((g) => (
            <Card key={g.id} className="border-warning/40">
              <CardHeader
                title={`${g.requesterName} pede acesso por ${g.durationHours} hora${g.durationHours > 1 ? "s" : ""}`}
                description={`${g.requesterEmail} · pedido em ${dateTime(g.requestedAt)}`}
                actions={<Badge tone="warning" icon>Aguardando decisão</Badge>}
              />
              <CardBody>
                <p className="text-sm text-foreground-secondary">
                  <span className="font-medium text-foreground">Motivo:</span> {g.reason}
                </p>
                {director ? (
                  <div className="mt-4 flex flex-wrap gap-2">
                    <Button
                      variant="primary"
                      disabled={act.isPending}
                      onClick={() => {
                        if (confirm(`Liberar acesso somente leitura para ${g.requesterName} por ${g.durationHours} hora(s)?`)) act.mutate({ id: g.id, action: "approve" });
                      }}
                    >
                      <Check />
                      Liberar
                    </Button>
                    <Button disabled={act.isPending} onClick={() => act.mutate({ id: g.id, action: "deny" })}>
                      <X />
                      Recusar
                    </Button>
                  </div>
                ) : (
                  <p className="mt-3 text-xs text-muted-foreground">Só a Direção pode liberar ou recusar.</p>
                )}
              </CardBody>
            </Card>
          ))}

          {active.map((g) => (
            <Card key={g.id}>
              <CardHeader
                title={`${g.requesterName} tem acesso até ${dateTime(g.expiresAt)}`}
                description={`Liberado por ${g.decidedByName ?? "Direção"} em ${dateTime(g.decidedAt)} · ${g.actions} tela(s) aberta(s)${g.lastActionAt ? `, a última em ${dateTime(g.lastActionAt)}` : ""}`}
                actions={
                  <>
                    <Badge tone="good" icon>Liberado</Badge>
                    {director ? (
                      <Button
                        size="sm"
                        variant="danger"
                        disabled={act.isPending}
                        onClick={() => {
                          if (confirm(`Encerrar agora o acesso de ${g.requesterName}?`)) act.mutate({ id: g.id, action: "revoke" });
                        }}
                      >
                        <ShieldOff />
                        Revogar agora
                      </Button>
                    ) : null}
                  </>
                }
              />
              <CardBody>
                <p className="text-sm text-foreground-secondary">
                  <span className="font-medium text-foreground">Motivo:</span> {g.reason}
                </p>
              </CardBody>
            </Card>
          ))}

          {history.length ? (
            <Card>
              <CardHeader title="Histórico" description="Pedidos dos últimos 30 dias e o que o suporte fez durante cada acesso." />
              <Table>
                <thead>
                  <tr>
                    <Th>Suporte</Th>
                    <Th>Situação</Th>
                    <Th>Decisão</Th>
                    <Th className="text-right">Telas abertas</Th>
                  </tr>
                </thead>
                <tbody>
                  {history.map((g) => (
                    <Tr key={g.id}>
                      <Td>
                        <p className="font-medium">{g.requesterName}</p>
                        <p className="max-w-md truncate text-xs text-muted-foreground" title={g.reason}>
                          {g.reason}
                        </p>
                      </Td>
                      <Td>
                        <Badge tone={statusBadge[g.status].tone} icon>
                          {statusBadge[g.status].label}
                        </Badge>
                      </Td>
                      <Td className="text-foreground-secondary">
                        {g.decidedAt ? `${g.decidedByName ?? "Direção"} · ${dateTime(g.decidedAt)}` : dateTime(g.requestedAt)}
                      </Td>
                      <Td className="text-right tabular-nums">{g.actions}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </Card>
          ) : null}
        </div>
      )}
    </>
  );
}
