import { useQuery } from "@tanstack/react-query";
import {
  Alert,
  ApiError,
  Badge,
  BarList,
  Button,
  Card,
  CardBody,
  CardHeader,
  ChartCard,
  EmptyState,
  format,
  LineTrend,
  PageHeader,
  SegmentedBar,
  StatTile,
  Table,
  Td,
  Th,
  Tr,
  type StatDelta,
} from "@sinedu/ui";
import { AlertTriangle, CalendarRange, CheckCircle2, ClipboardList, GraduationCap, Info, Plus, UserRound, XCircle } from "lucide-react";
import { Link, useNavigate } from "react-router";
import { analyticsApi } from "../lib/analytics-api";
import { useHasRole } from "../lib/session";
import { ExportMenu } from "./analytics/export-menu";
import { FiltersBar } from "./analytics/filters";
import { useAnalyticsFilters } from "./analytics/use-analytics-filters";
import { WelcomePage } from "./welcome-page";

function delta(current: number, previous: number, upIs: StatDelta["upIs"], comparedTo: string): StatDelta | undefined {
  if (previous === 0) return undefined;
  const diff = current - previous;
  const ratio = previous ? diff / previous : 0;
  return {
    label: `${diff > 0 ? "+" : diff < 0 ? "−" : ""}${format.percent(Math.abs(ratio))}`,
    direction: diff > 0 ? "up" : diff < 0 ? "down" : "flat",
    upIs,
    comparedTo,
  };
}

export function DashboardPage() {
  const pedagogical = useHasRole("DIRETOR", "PEDAGOGO");
  return pedagogical ? <PedagogicalDashboard /> : <WelcomePage />;
}

function PedagogicalDashboard() {
  const navigate = useNavigate();
  const { filter, options, set, ready } = useAnalyticsFilters();
  const { data, isFetching, error } = useQuery({
    queryKey: ["dashboard", filter],
    queryFn: () => analyticsApi.dashboard(filter),
    placeholderData: (previous) => previous,
    enabled: ready,
  });
  const noYear = options !== undefined && options.years.length === 0;

  return (
    <>
      <PageHeader
        title="Visão geral"
        eyebrow={data ? <Badge tone="neutral">{data.period.label} · {format.shortDate(`${data.period.start}T12:00:00Z`)} a {format.shortDate(`${data.period.end}T12:00:00Z`)}</Badge> : undefined}
        description="Indicadores pedagógicos do período. Contam apenas ocorrências registradas, pela data do fato e pela turma da época."
        actions={
          <>
            {ready ? <ExportMenu filter={filter} /> : null}
            <Button variant="primary" onClick={() => navigate("/ocorrencias/nova")}>
              <Plus />
              Nova ocorrência
            </Button>
          </>
        }
      />

      {/* Filtros: uma linha, acima de tudo que eles afetam */}
      <FiltersBar filter={filter} options={options} set={set} />

      {noYear ? (
        <EmptyState icon={CalendarRange} title="Nenhum ano letivo cadastrado" description="Cadastre o ano letivo e os bimestres em Administração para ver os indicadores." />
      ) : error ? (
        <Alert tone="warning" title={error instanceof ApiError ? error.message : "Não foi possível carregar os indicadores."} />
      ) : !data ? (
        <DashboardSkeleton />
      ) : (
        // Ao trocar filtros, mantém o quadro anterior esmaecido em vez de piscar um esqueleto.
        <div className={isFetching ? "opacity-60 transition-opacity" : "transition-opacity"} aria-busy={isFetching}>
          <section aria-label="Resumo" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatTile
              label="Alunos matriculados"
              value={format.integer(data.enrolled)}
              icon={GraduationCap}
              hint={`${data.byClassroom.length} turmas no recorte`}
              delta={delta(data.enrolled, data.enrolledPrevious, "neutral", `vs ${data.period.previousLabel}`)}
            />
            <StatTile
              label="Ocorrências registradas"
              value={format.integer(data.occurrences)}
              icon={ClipboardList}
              delta={delta(data.occurrences, data.occurrencesPrevious, "neutral", `vs ${data.period.previousLabel}`)}
            />
            <StatTile
              label="Alunos com ao menos 1 ocorrência"
              value={format.integer(data.studentsWithOccurrence)}
              icon={UserRound}
              hint={data.enrolled ? `${format.percent(data.studentsWithOccurrence / data.enrolled, 1)} dos matriculados` : "Sem matrículas no recorte"}
            />
            <StatTile
              label="Ocorrências concluídas"
              value={data.occurrences ? format.percent(data.status.concluded / data.occurrences) : "—"}
              icon={CheckCircle2}
              hint={`${format.integer(data.status.concluded)} de ${format.integer(data.occurrences)}`}
            />
          </section>

          <section className="mt-4 grid gap-4 xl:grid-cols-3">
            <ChartCard
              className="xl:col-span-2"
              title="Evolução semanal"
              description={`Ocorrências registradas por semana — ${data.period.label} comparado ao ${data.period.previousLabel}`}
              legend={[
                { label: data.period.label, color: "var(--series-1)", shape: "line" },
                { label: data.period.previousLabel, color: "var(--series-prev)", shape: "line" },
              ]}
              table={{
                columns: [{ label: "Semana" }, { label: data.period.label, align: "right" }, { label: data.period.previousLabel, align: "right" }],
                rows: data.weekly.map((w) => [w.week, w.current ?? "—", w.previous ?? "—"]),
              }}
            >
              <LineTrend
                data={data.weekly}
                xKey="week"
                series={[
                  { key: "previous", label: data.period.previousLabel, color: "var(--series-prev)" },
                  { key: "current", label: data.period.label, color: "var(--series-1)", primary: true },
                ]}
                formatValue={format.integer}
              />
            </ChartCard>

            <ChartCard
              title="Ocorrências por categoria"
              description={data.period.label}
              table={{
                columns: [{ label: "Categoria" }, { label: "Ocorrências", align: "right" }],
                rows: data.byCategory.map((c) => [c.label, c.value]),
              }}
            >
              <BarList data={data.byCategory} seriesLabel="Ocorrências" formatValue={format.integer} labelWidth={150} />
            </ChartCard>
          </section>

          <section className="mt-4 grid gap-4 xl:grid-cols-3">
            <ChartCard
              className="xl:col-span-2"
              title="Comparação entre turmas"
              description="Ocorrências por 100 alunos matriculados — considera o tamanho de cada turma"
              table={{
                columns: [
                  { label: "Turma" },
                  { label: "Turno" },
                  { label: "Matriculados", align: "right" },
                  { label: "Ocorrências", align: "right" },
                  { label: "Por 100 alunos", align: "right" },
                ],
                rows: data.byClassroom.map((c) => [c.classroom, c.shift, c.enrolled, c.occurrences, c.ratePer100 === null ? "—" : format.decimal(c.ratePer100)]),
              }}
              footer="A taxa serve para acompanhar tendências e orientar ações pedagógicas. Não classifica turmas nem alunos."
            >
              <BarList
                data={data.byClassroom
                  .filter((c) => c.ratePer100 !== null)
                  .map((c) => ({ label: `${c.classroom} · ${c.shift}`, value: c.ratePer100 ?? 0 }))
                  .sort((a, b) => b.value - a.value)}
                seriesLabel="Ocorrências por 100 alunos"
                formatValue={format.decimal}
                labelWidth={150}
              />
            </ChartCard>

            <div className="flex flex-col gap-4">
              <Card>
                <CardHeader title="Situação das ocorrências" description={data.period.label} />
                <CardBody>
                  <SegmentedBar
                    ariaLabel="Situação das ocorrências"
                    segments={[
                      { label: "Registradas", value: data.status.registered, color: "var(--series-1)" },
                      { label: "Em acompanhamento", value: data.status.followUp, color: "var(--series-2)" },
                      { label: "Concluídas", value: data.status.concluded, color: "var(--series-3)" },
                    ]}
                  />
                </CardBody>
              </Card>

              <Card>
                <CardHeader title="Comunicações aos responsáveis" description="Ligadas às ocorrências do recorte" />
                <CardBody className="space-y-3">
                  <NotificationRow icon={CheckCircle2} tone="text-good-text" label="Enviadas" value={data.notifications.sent} />
                  <NotificationRow icon={AlertTriangle} tone="text-warning-text" label="Pendentes" value={data.notifications.pending} />
                  <NotificationRow icon={XCircle} tone="text-critical-text" label="Com falha" value={data.notifications.failed} action="Ver falhas" to="/comunicacoes" />
                </CardBody>
              </Card>
            </div>
          </section>

          <Card className="mt-4">
            <CardHeader
              title="Acompanhamentos em aberto"
              description="Ocorrências registradas ou em acompanhamento que aguardam providência"
              actions={
                <Link to="/ocorrencias">
                  <Button size="sm">Ver todas</Button>
                </Link>
              }
            />
            <div className="mt-4">
              <Table>
                <thead>
                  <tr>
                    <Th>Aluno</Th>
                    <Th>Turma</Th>
                    <Th>Categoria</Th>
                    <Th>Data do fato</Th>
                    <Th>Responsável</Th>
                    <Th>Situação</Th>
                  </tr>
                </thead>
                <tbody>
                  {data.followUps.length === 0 ? (
                    <Tr>
                      <Td colSpan={6} className="text-muted-foreground">
                        Nenhum acompanhamento em aberto no recorte.
                      </Td>
                    </Tr>
                  ) : null}
                  {data.followUps.map((f) => (
                    <Tr key={f.id} className="cursor-pointer" onClick={() => navigate(`/ocorrencias/${f.id}`)}>
                      <Td className="font-medium">{f.student}</Td>
                      <Td className="text-foreground-secondary">{f.classroom}</Td>
                      <Td className="text-foreground-secondary">{f.category}</Td>
                      <Td className="text-foreground-secondary tabular-nums">{format.shortDate(f.occurredAt)}</Td>
                      <Td className="text-foreground-secondary">{f.owner}</Td>
                      <Td>
                        {f.status === "EmAcompanhamento" ? (
                          <Badge tone="warning" icon>
                            Em acompanhamento
                          </Badge>
                        ) : (
                          <Badge tone="info" icon>
                            Registrada
                          </Badge>
                        )}
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </Card>

          <p className="mt-4 flex items-start gap-1.5 text-xs text-muted-foreground">
            <Info className="mt-0.5 size-3.5 shrink-0" aria-hidden />
            {data.method}
          </p>
        </div>
      )}
    </>
  );
}

function NotificationRow({
  icon: Icon,
  tone,
  label,
  value,
  action,
  to,
}: {
  icon: typeof CheckCircle2;
  tone: string;
  label: string;
  value: number;
  action?: string;
  to?: string;
}) {
  return (
    <div className="flex items-center gap-3">
      <Icon className={`size-4 ${tone}`} aria-hidden />
      <span className="flex-1 text-sm text-foreground-secondary">{label}</span>
      {action && to && value > 0 ? (
        <Link to={to}>
          <Button size="sm" variant="ghost" className="h-7 px-2 text-xs">
            {action}
          </Button>
        </Link>
      ) : null}
      <span className="w-8 text-right text-sm font-semibold text-foreground tabular-nums">{value}</span>
    </div>
  );
}

function DashboardSkeleton() {
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-busy="true" aria-label="Carregando indicadores">
      {Array.from({ length: 4 }, (_, i) => (
        <div key={i} className="h-[132px] animate-pulse rounded-(--radius-card) border border-border bg-surface" />
      ))}
    </div>
  );
}
