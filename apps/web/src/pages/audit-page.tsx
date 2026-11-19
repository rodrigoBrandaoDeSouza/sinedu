import { useQuery } from "@tanstack/react-query";
import { api, Badge, Button, Card, CardBody, EmptyState, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { ScrollText } from "lucide-react";
import { Link, useSearchParams } from "react-router";

interface AuditItem {
  id: number;
  occurredAt: string;
  action: string;
  result: "Allowed" | "Denied";
  user: { id: string; name: string; email: string | null } | null;
  student: { id: string; name: string } | null;
  isSupportSession: boolean;
  ip: string | null;
}

interface AuditPage {
  total: number;
  page: number;
  pageSize: number;
  items: AuditItem[];
}

/** Rótulos das ações (sem conteúdo: só o fato). Ação desconhecida aparece com o código. */
const actionLabels: Record<string, string> = {
  health_viewed: "Abriu a ficha de Saúde",
  protection_viewed: "Abriu Proteção/Sigilo",
  protection_created: "Criou registro de Proteção/Sigilo",
  protection_updated: "Alterou registro de Proteção/Sigilo",
  protection_revoked: "Revogou registro de Proteção/Sigilo",
  protected_exported: "Exportação excepcional de dados protegidos",
  protected_attachment_downloaded: "Baixou anexo protegido",
  protected_attachment_uploaded: "Enviou anexo protegido",
  protected_attachment_removed: "Removeu anexo protegido",
  protected_access_denied: "Acesso negado a dado protegido",
  protected_pending_viewed: "Viu pendências sigilosas",
  protected_keys_rewrapped: "Chave de criptografia rotacionada",
  login_succeeded: "Entrou no sistema",
  login_denied: "Entrada negada",
  logout: "Saiu do sistema",
  role_granted: "Concedeu papel",
  role_revoked: "Revogou papel",
  role_grant_denied: "Tentou conceder papel sem permissão",
  role_revoke_denied: "Tentou revogar papel sem permissão",
  readonly_grants_changed: "Alterou turmas do perfil Consulta",
  student_access_denied: "Acesso negado a aluno",
  support_approved: "Liberou acesso de suporte",
  support_denied: "Recusou acesso de suporte",
  support_revoked: "Revogou acesso de suporte",
  support_session: "Tela aberta pelo suporte",
  platform_mfa_reset: "Plataforma reiniciou o MFA da conta",
  platform_account_unlocked: "Plataforma desbloqueou a conta",
  student_created: "Cadastrou aluno",
  student_updated: "Alterou cadastro de aluno",
  student_moved: "Remanejou aluno",
  guardian_added: "Adicionou responsável",
  guardian_updated: "Corrigiu responsável",
  guardian_unlinked: "Desvinculou responsável",
  guardian_contact_updated: "Alterou contato de responsável",
  guardian_contact_authorized: "Registrou autorização de contato",
  occurrence_created: "Criou ocorrência",
  occurrence_registered: "Registrou ocorrência",
  occurrence_updated: "Alterou ocorrência",
  occurrence_ata_generated: "Gerou ata",
  communication_authorized: "Autorizou comunicação",
  import_uploaded: "Enviou planilha",
  import_confirmed: "Confirmou importação",
  analytics_exported: "Exportou indicadores",
  closing_export: "Baixou a exportação de encerramento",
  school_logo_updated: "Alterou o logo",
};

const categories = [
  { value: "protegidos", label: "Saúde e Proteção/Sigilo" },
  { value: "acessos", label: "Entradas e papéis" },
  { value: "suporte", label: "Acesso de suporte" },
  { value: "tudo", label: "Tudo" },
];

const dateTime = (iso: string) => new Date(iso).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "medium" });

/** Matriz: "Auditoria de acessos ao registro" — só a Direção (P9). Mostra o fato, nunca o conteúdo. */
export function AuditPage() {
  const [params, setParams] = useSearchParams();
  const category = params.get("categoria") ?? "protegidos";
  const studentId = params.get("aluno");
  const from = params.get("de") ?? "";
  const to = params.get("ate") ?? "";
  const page = Number(params.get("pagina") ?? 1);

  const set = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params);
    for (const [k, v] of Object.entries(changes)) if (v) next.set(k, v); else next.delete(k);
    if (!("pagina" in changes)) next.delete("pagina");
    setParams(next, { replace: true });
  };

  const query = new URLSearchParams();
  if (category) query.set("category", category);
  if (studentId) query.set("studentId", studentId);
  if (from) query.set("from", from);
  if (to) query.set("to", to);
  query.set("page", String(page));
  const audit = useQuery({ queryKey: ["audit", query.toString()], queryFn: () => api.get<AuditPage>(`/api/audit?${query}`) });
  const data = audit.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <>
      <PageHeader
        title="Auditoria"
        description="Quem fez o quê e quando. Registra só o fato, nunca o conteúdo. Visível apenas para a Direção."
      />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <Select label="Mostrar" className="w-60" value={category} onChange={(e) => set({ categoria: e.target.value })} options={categories} />
        <Input label="De" type="date" className="w-40" value={from} onChange={(e) => set({ de: e.target.value })} />
        <Input label="Até" type="date" className="w-40" value={to} onChange={(e) => set({ ate: e.target.value })} />
        {studentId ? (
          <Button variant="ghost" onClick={() => set({ aluno: null })}>
            Ver todos os alunos
          </Button>
        ) : null}
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
                <Th>Aluno</Th>
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
                      {e.isSupportSession ? <Badge tone="warning">Suporte</Badge> : null}
                    </span>
                  </Td>
                  <Td>
                    {e.student ? (
                      <Link to={`/alunos/${e.student.id}`} className="text-foreground hover:text-primary">
                        {e.student.name}
                      </Link>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </Td>
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
