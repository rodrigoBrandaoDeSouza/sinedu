import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, CardBody, CardHeader, ChartCard, EmptyState, format, LineTrend, PageHeader, StatTile, Table, Td, Th, Tr } from "@sinedu/ui";
import { AlertTriangle, Building2, CircleDollarSign, DatabaseZap, GraduationCap, MailWarning, Plus, RefreshCw, UsersRound } from "lucide-react";
import { Link, useNavigate } from "react-router";
import { formatBytes, metricsApi, type TenantMetrics } from "../lib/metrics-api";
import { useIsSuperAdmin } from "../lib/session";

/**
 * Visão geral da plataforma com as métricas coletadas das escolas (P-10). Só contagens: o painel nunca recebe
 * nomes, identificadores ou dados de alunos, nem qualquer contagem de Saúde/Proteção.
 */
export function OverviewPage() {
  const navigate = useNavigate();
  const superAdmin = useIsSuperAdmin();
  const client = useQueryClient();
  const { data, error } = useQuery({ queryKey: ["metrics"], queryFn: metricsApi.overview });
  const collect = useMutation({ mutationFn: metricsApi.collect, onSuccess: () => client.invalidateQueries({ queryKey: ["metrics"] }) });

  const active = data?.tenants.filter((t) => t.status === "Active") ?? [];
  const students = active.reduce((s, t) => s + t.activeStudents, 0);
  const members = active.reduce((s, t) => s + t.activeMembers, 0);
  const failedEmails = active.reduce((s, t) => s + t.emailsFailedOpen, 0);
  const alerts = data ? operationalAlerts(data.tenants) : [];

  return (
    <>
      <PageHeader
        title="Visão geral"
        description="Escolas e uso da plataforma. O painel nunca exibe dados de alunos — só contagens por escola."
        eyebrow={data?.collectedAt ? <Badge tone="neutral">Coleta de {format.shortDate(data.collectedAt)}</Badge> : undefined}
        actions={
          superAdmin ? (
            <>
              <Button onClick={() => collect.mutate()} disabled={collect.isPending}>
                <RefreshCw />
                {collect.isPending ? "Coletando…" : "Coletar agora"}
              </Button>
              <Button variant="primary" onClick={() => navigate("/escolas?nova=1")}>
                <Plus />
                Nova escola
              </Button>
            </>
          ) : undefined
        }
      />

      {error ? <Alert tone="critical" title="Não foi possível carregar as métricas." /> : null}
      {!data ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-busy="true" aria-label="Carregando">
          {Array.from({ length: 4 }, (_, i) => (
            <div key={i} className="h-[132px] animate-pulse rounded-(--radius-card) border border-border bg-surface" />
          ))}
        </div>
      ) : data.tenants.length === 0 ? (
        <EmptyState icon={Building2} title="Nenhuma escola cadastrada" description="Cadastre a primeira escola para começar." />
      ) : (
        <>
          <section aria-label="Resumo" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatTile
              label="Escolas ativas"
              value={active.length}
              icon={Building2}
              hint={`${data.tenants.filter((t) => t.status === "Suspended").length} suspensa(s) · ${data.tenants.filter((t) => t.status.startsWith("Provisioning")).length} em provisionamento`}
            />
            <StatTile label="Alunos ativos" value={format.integer(students)} icon={GraduationCap} hint="Soma das escolas ativas" />
            <StatTile label="Usuários ativos" value={format.integer(members)} icon={UsersRound} hint="Equipes das escolas" />
            <StatTile label="E-mails com falha" value={format.integer(failedEmails)} icon={MailWarning} hint="Aguardando reenvio nas escolas" />
          </section>

          <section className="mt-4 grid gap-4 xl:grid-cols-3">
            <ChartCard
              className="xl:col-span-2"
              title="Alunos ativos na plataforma"
              description="Soma das escolas, por dia de coleta"
              table={{
                columns: [{ label: "Dia" }, { label: "Alunos ativos", align: "right" }],
                rows: data.trend.map((p) => [format.shortDate(`${p.day}T12:00:00Z`), format.integer(p.students)]),
              }}
            >
              <LineTrend
                data={data.trend.map((p) => ({ ...p, label: format.shortDate(`${p.day}T12:00:00Z`) }))}
                xKey="label"
                series={[{ key: "students", label: "Alunos ativos", color: "var(--series-1)", primary: true }]}
                formatValue={format.integer}
              />
            </ChartCard>

            <Card>
              <CardHeader title="Alertas de operação" description="Atualização de schema, coleta e e-mails" />
              <CardBody>
                {alerts.length === 0 ? (
                  <p className="text-sm text-muted-foreground">Nada pendente.</p>
                ) : (
                  <ul className="space-y-3">
                    {alerts.map((a) => (
                      <li key={a.key} className="flex gap-3 rounded-xl border border-border bg-surface-raised p-3">
                        <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-warning-soft text-warning-text">
                          <a.icon className="size-4" aria-hidden />
                        </span>
                        <div className="min-w-0">
                          <p className="text-sm font-medium text-foreground">{a.title}</p>
                          <p className="mt-0.5 text-xs text-muted-foreground">{a.detail}</p>
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
                <div className="mt-4 flex items-center gap-2 text-xs text-muted-foreground">
                  <CircleDollarSign className="size-3.5" aria-hidden />
                  Faturamento aparece aqui quando contratos e faturas estiverem cadastrados (P-06 a P-08).
                </div>
              </CardBody>
            </Card>
          </section>

          <Card className="mt-4">
            <CardHeader
              title="Uso por escola"
              description="Contagens da última coleta. Nenhum dado de aluno é enviado ao painel."
              actions={
                <Link to="/escolas" className="text-sm font-medium text-primary hover:underline">
                  Gerenciar escolas
                </Link>
              }
            />
            <div className="mt-4">
              <Table>
                <thead>
                  <tr>
                    <Th>Escola</Th>
                    <Th className="text-right">Alunos</Th>
                    <Th className="text-right">Turmas</Th>
                    <Th className="text-right">Usuários</Th>
                    <Th className="text-right">Ocorrências (30 dias)</Th>
                    <Th className="text-right">E-mails no mês</Th>
                    <Th className="text-right">Armazenamento</Th>
                    <Th>Schema</Th>
                  </tr>
                </thead>
                <tbody>
                  {data.tenants.map((t) => (
                    <Tr key={t.tenantId}>
                      <Td>
                        <p className="font-medium text-foreground">{t.name}</p>
                        <p className="font-mono text-xs text-muted-foreground">{t.slug}</p>
                      </Td>
                      <Td className="text-right tabular-nums">{format.integer(t.activeStudents)}</Td>
                      <Td className="text-right tabular-nums">{t.activeClassrooms}</Td>
                      <Td className="text-right tabular-nums">{t.activeMembers}</Td>
                      <Td className="text-right tabular-nums">{t.occurrencesLast30Days}</Td>
                      <Td className="text-right tabular-nums">{t.emailsSentThisMonth}</Td>
                      <Td className="text-right tabular-nums">{formatBytes(t.commonStorageBytes)}</Td>
                      <Td>
                        {t.outdated ? (
                          <Badge tone="warning" icon>
                            Desatualizado
                          </Badge>
                        ) : (
                          <Badge tone="good" icon>
                            Atual
                          </Badge>
                        )}
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </Card>
        </>
      )}
    </>
  );
}

function operationalAlerts(tenants: TenantMetrics[]) {
  const list: { key: string; icon: typeof AlertTriangle; title: string; detail: string }[] = [];
  const outdated = tenants.filter((t) => t.outdated && (t.status === "Active" || t.status === "Suspended"));
  if (outdated.length) list.push({ key: "schema", icon: DatabaseZap, title: `${outdated.length} escola(s) com schema desatualizado`, detail: "Ficam em manutenção até a atualização em Escolas." });
  for (const t of tenants.filter((x) => x.error)) list.push({ key: `err-${t.tenantId}`, icon: AlertTriangle, title: `Coleta falhou: ${t.name}`, detail: t.error ?? "" });
  for (const t of tenants.filter((x) => x.emailsFailedOpen > 0)) list.push({ key: `mail-${t.tenantId}`, icon: MailWarning, title: `${t.emailsFailedOpen} e-mail(s) com falha: ${t.name}`, detail: "A escola pode reenviar em Comunicações." });
  return list;
}
