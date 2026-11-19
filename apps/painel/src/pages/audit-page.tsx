import { useQuery } from "@tanstack/react-query";
import { api, Badge, Button, Card, CardBody, EmptyState, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { ScrollText } from "lucide-react";
import { useSearchParams } from "react-router";

interface AuditItem {
  id: number;
  occurredAt: string;
  action: string;
  result: "Allowed" | "Denied";
  user: { id: string; name: string; email: string | null } | null;
  resourceType: string | null;
  resourceId: string | null;
  school: string | null;
  ip: string | null;
}

interface AuditPage {
  total: number;
  page: number;
  pageSize: number;
  items: AuditItem[];
}

const actionLabels: Record<string, string> = {
  tenant_created: "Cadastrou escola",
  tenant_provisioned: "Escola provisionada",
  tenant_invitation_sent: "Convidou o primeiro Diretor",
  tenant_suspended: "Suspendeu escola",
  tenant_reactivated: "Reativou escola",
  tenant_closing_started: "Iniciou o encerramento da escola",
  tenant_closing_cancelled: "Desfez o encerramento",
  tenant_destroyed: "Destruiu os dados da escola",
  tenant_migrated: "Atualizou o banco da escola",
  tenant_network_changed: "Mudou a rede da escola",
  tenant_smtp_updated: "Alterou o SMTP da escola",
  tenant_smtp_removed: "Removeu o SMTP da escola",
  metrics_collected: "Coletou métricas",
  network_created: "Criou rede",
  network_updated: "Alterou rede",
  network_deleted: "Excluiu rede",
  platform_first_admin_invited: "Convidou o primeiro super admin",
  platform_account_invited: "Convidou conta da plataforma",
  platform_account_revoked: "Retirou conta da plataforma",
  account_unlocked: "Desbloqueou conta",
  account_mfa_reset: "Reiniciou o MFA de uma conta",
  login_succeeded: "Entrou",
  login_failed: "Senha incorreta",
  login_locked: "Conta bloqueada por tentativas",
  mfa_enabled: "Ativou o MFA",
  mfa_failed: "Código MFA incorreto",
  mfa_recovery_code_used: "Usou código de recuperação",
  password_reset_requested: "Pediu nova senha",
  password_reset: "Trocou a senha",
  invitation_accepted: "Aceitou convite",
  sso_issued: "Entrou em escola/painel",
  sso_denied: "Entrada em escola negada",
  logout: "Saiu",
  plan_created: "Criou plano",
  plan_updated: "Alterou plano",
  contract_created: "Criou contrato",
  contract_updated: "Alterou contrato",
  invoices_generated: "Gerou faturas do mês",
  invoice_paid: "Deu baixa em fatura",
  invoice_fiscal_note: "Registrou nota fiscal",
  invoice_cancelled: "Cancelou fatura",
  support_requested: "Pediu acesso de suporte",
  support_cancelled: "Retirou pedido de suporte",
  support_approved: "Escola liberou o suporte",
  support_denied: "Escola recusou o suporte",
  support_revoked: "Escola revogou o suporte",
};

const categories = [
  { value: "tudo", label: "Tudo" },
  { value: "escolas", label: "Escolas" },
  { value: "contas", label: "Contas e acessos" },
  { value: "cobranca", label: "Cobrança" },
  { value: "suporte", label: "Suporte" },
];

const dateTime = (iso: string) => new Date(iso).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "medium" });

/** Auditoria da plataforma (matriz: só SUPER_ADMIN). Não existe dado de aluno aqui. */
export function AuditPage() {
  const [params, setParams] = useSearchParams();
  const category = params.get("categoria") ?? "tudo";
  const from = params.get("de") ?? "";
  const to = params.get("ate") ?? "";
  const page = Number(params.get("pagina") ?? 1);
  const set = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) if (v) next.set(k, v); else next.delete(k);
    if (!("pagina" in changes)) next.delete("pagina");
    setParams(next, { replace: true });
  };

  const query = new URLSearchParams({ category, page: String(page) });
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  const audit = useQuery({ queryKey: ["platform-audit", query.toString()], queryFn: () => api.get<AuditPage>(`/api/audit?${query}`) });
  const data = audit.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <>
      <PageHeader title="Auditoria" description="Ações no painel e no login único: quem, o quê, quando e em qual escola." />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <Select label="Mostrar" className="w-52" value={category} onChange={(e) => set({ categoria: e.target.value })} options={categories} />
        <Input label="De" type="date" className="w-40" value={from} onChange={(e) => set({ de: e.target.value })} />
        <Input label="Até" type="date" className="w-40" value={to} onChange={(e) => set({ ate: e.target.value })} />
      </div>

      {data?.total === 0 ? (
        <EmptyState icon={ScrollText} title="Nada registrado com esses filtros" description="Mude o período ou o tipo de evento." />
      ) : (
        <Card>
          <Table>
            <thead>
              <tr>
                <Th>Quando</Th>
                <Th>Quem</Th>
                <Th>O quê</Th>
                <Th>Escola</Th>
                <Th>IP</Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.map((e) => (
                <Tr key={e.id}>
                  <Td className="whitespace-nowrap tabular-nums text-foreground-secondary">{dateTime(e.occurredAt)}</Td>
                  <Td>
                    <p className="font-medium">{e.user?.name ?? "Sistema"}</p>
                    {e.user?.email ? <p className="text-xs text-muted-foreground">{e.user.email}</p> : null}
                  </Td>
                  <Td>
                    <span className="flex flex-wrap items-center gap-1.5">
                      {actionLabels[e.action] ?? e.action}
                      {e.result === "Denied" ? (
                        <Badge tone="critical" icon>
                          Negado
                        </Badge>
                      ) : null}
                    </span>
                  </Td>
                  <Td className="text-foreground-secondary">{e.school ?? "—"}</Td>
                  <Td className="font-mono text-xs text-muted-foreground">{e.ip ?? "—"}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          {data && pages > 1 ? (
            <CardBody className="flex items-center justify-between border-t border-border py-3">
              <span className="text-sm text-muted-foreground">
                Página {data.page} de {pages} · {data.total.toLocaleString("pt-BR")} eventos
              </span>
              <span className="flex gap-2">
                <Button size="sm" disabled={page <= 1} onClick={() => set({ pagina: String(page - 1) })}>
                  Anterior
                </Button>
                <Button size="sm" disabled={page >= pages} onClick={() => set({ pagina: String(page + 1) })}>
                  Próxima
                </Button>
              </span>
            </CardBody>
          ) : null}
        </Card>
      )}
    </>
  );
}
