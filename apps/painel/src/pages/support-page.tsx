import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardBody, CardHeader, EmptyState, PageHeader, Select, Table, Td, Textarea, Th, Tr } from "@sinedu/ui";
import { ExternalLink, LifeBuoy, Send, X } from "lucide-react";
import { useState, type FormEvent } from "react";

type RequestStatus = "Pending" | "Approved" | "Denied" | "Cancelled" | "Revoked" | "Expired";

interface SupportRequestRow {
  id: string;
  tenant: { id: string; slug: string; name: string };
  requester: string;
  mine: boolean;
  reason: string;
  durationHours: number;
  status: RequestStatus;
  createdAt: string;
  decidedAt: string | null;
  decidedByName: string | null;
  expiresAt: string | null;
  url: string | null;
}

interface TenantOption {
  id: string;
  slug: string;
  displayName: string;
  status: string;
}

const statusBadge: Record<RequestStatus, { label: string; tone: "warning" | "good" | "neutral" | "critical" | "info" }> = {
  Pending: { label: "Aguardando a escola", tone: "warning" },
  Approved: { label: "Liberado", tone: "good" },
  Denied: { label: "Recusado", tone: "neutral" },
  Cancelled: { label: "Retirado", tone: "neutral" },
  Revoked: { label: "Revogado pela escola", tone: "critical" },
  Expired: { label: "Encerrado (prazo)", tone: "info" },
};

const durations = [1, 2, 4, 8, 24];
const dateTime = (value: string | null) => (value ? new Date(value).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" }) : "—");

/**
 * P-11 / D17: o suporte pede acesso a uma escola; os diretores recebem e-mail e só a Direção libera.
 * Liberado, o acesso é somente leitura de dados comuns e vale até o prazo — entra-se pelo endereço da escola.
 */
export function SupportPage() {
  const client = useQueryClient();
  const requests = useQuery({
    queryKey: ["support-requests"],
    queryFn: () => api.get<SupportRequestRow[]>("/api/support/requests"),
    refetchInterval: 30_000,
  });
  const cancel = useMutation({
    mutationFn: (id: string) => api.post(`/api/support/requests/${id}/cancel`),
    onSettled: () => client.invalidateQueries({ queryKey: ["support-requests"] }),
  });

  return (
    <>
      <PageHeader
        title="Acessos de suporte"
        description="Para entrar numa escola, peça acesso. A Direção da escola decide; o acesso é somente leitura e dura no máximo 24 horas."
      />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,380px)_minmax(0,1fr)]">
        <NewRequestCard />

        <Card>
          <CardHeader title="Pedidos" description="Atualiza sozinho. Ao ser liberado, use “Entrar na escola”." />
          {cancel.error ? (
            <Alert tone="critical" title={cancel.error instanceof ApiError ? cancel.error.message : "Não foi possível retirar."} className="mx-5 mt-4" />
          ) : null}
          {requests.data?.length === 0 ? (
            <CardBody>
              <EmptyState icon={LifeBuoy} title="Nenhum pedido ainda" description="Os pedidos feitos aparecem aqui, com a decisão da escola." />
            </CardBody>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Escola</Th>
                  <Th>Situação</Th>
                  <Th>Prazo</Th>
                  <Th className="text-right">Ação</Th>
                </tr>
              </thead>
              <tbody>
                {(requests.data ?? []).map((r) => (
                  <Tr key={r.id}>
                    <Td>
                      <p className="font-medium">{r.tenant.name}</p>
                      <p className="max-w-sm truncate text-xs text-muted-foreground" title={r.reason}>
                        {r.mine ? "" : `${r.requester} · `}
                        {r.reason}
                      </p>
                    </Td>
                    <Td>
                      <Badge tone={statusBadge[r.status].tone} icon>
                        {statusBadge[r.status].label}
                      </Badge>
                      {r.decidedByName ? <p className="mt-1 text-xs text-muted-foreground">por {r.decidedByName}</p> : null}
                    </Td>
                    <Td className="text-foreground-secondary tabular-nums">
                      {r.status === "Approved" ? `até ${dateTime(r.expiresAt)}` : r.status === "Pending" ? `${r.durationHours} h pedidas` : dateTime(r.decidedAt ?? r.createdAt)}
                    </Td>
                    <Td className="text-right">
                      {r.url ? (
                        <a
                          href={r.url}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="inline-flex h-8 items-center gap-1.5 rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary-hover"
                        >
                          Entrar na escola
                          <ExternalLink className="size-3.5" aria-hidden />
                        </a>
                      ) : r.status === "Pending" && r.mine ? (
                        <Button size="sm" variant="ghost" disabled={cancel.isPending} onClick={() => cancel.mutate(r.id)}>
                          <X />
                          Retirar
                        </Button>
                      ) : null}
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </Card>
      </div>
    </>
  );
}

function NewRequestCard() {
  const client = useQueryClient();
  const [error, setError] = useState<string>();
  const [sent, setSent] = useState(false);
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: () => api.get<TenantOption[]>("/api/tenants") });
  const active = (tenants.data ?? []).filter((t) => t.status === "Active");

  const create = useMutation({
    mutationFn: (body: { tenantId: string; reason: string; durationHours: number }) => api.post("/api/support/requests", body),
    onSuccess: () => {
      setSent(true);
      setError(undefined);
      client.invalidateQueries({ queryKey: ["support-requests"] });
    },
    onError: (e) => setError(e instanceof ApiError ? e.message : "Não foi possível enviar o pedido."),
  });

  function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const form = new FormData(e.currentTarget);
    const tenantId = String(form.get("tenantId") ?? "");
    const reason = String(form.get("reason") ?? "").trim();
    if (!tenantId) return setError("Escolha a escola.");
    if (reason.length < 10) return setError("Explique o motivo em pelo menos 10 caracteres.");
    setSent(false);
    create.mutate({ tenantId, reason, durationHours: Number(form.get("durationHours")) });
    e.currentTarget.reset();
  }

  return (
    <Card className="self-start">
      <CardHeader title="Pedir acesso" description="Os diretores da escola recebem o pedido por e-mail." />
      <CardBody>
        <form onSubmit={submit} className="space-y-4" noValidate>
          {error ? <Alert tone="critical" title={error} /> : null}
          {sent ? <Alert tone="good" title="Pedido enviado. Aguarde a decisão da escola." /> : null}
          {active.length ? (
            <Select label="Escola" name="tenantId" options={active.map((t) => ({ value: t.id, label: `${t.displayName} (${t.slug})` }))} />
          ) : (
            <Alert tone="info" title="Nenhuma escola ativa." />
          )}
          <Select label="Duração" name="durationHours" defaultValue="2" options={durations.map((h) => ({ value: String(h), label: `${h} hora${h > 1 ? "s" : ""}` }))} />
          <Textarea
            label="Motivo"
            name="reason"
            maxLength={500}
            rows={4}
            hint="Descreva o problema a investigar. Não escreva nomes ou dados de alunos."
          />
          <Button type="submit" variant="primary" disabled={create.isPending || !active.length}>
            <Send />
            {create.isPending ? "Enviando…" : "Enviar pedido"}
          </Button>
        </form>
      </CardBody>
    </Card>
  );
}
