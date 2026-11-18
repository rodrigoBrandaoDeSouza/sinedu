import { useQuery } from "@tanstack/react-query";
import {
  Badge,
  BarList,
  Button,
  Card,
  CardBody,
  CardHeader,
  ChartCard,
  format,
  LineTrend,
  PageHeader,
  SegmentedBar,
  Select,
  StatTile,
  Table,
  Td,
  Th,
  Tr,
  type StatDelta,
} from "@sinedu/ui";
import { AlertTriangle, CheckCircle2, ClipboardList, Download, GraduationCap, Plus, UserRound, XCircle } from "lucide-react";
import { useState } from "react";
import { fetchDashboard, type DashboardFilters } from "../data/dashboard";

const yearOptions = [
  { value: "2026", label: "2026" },
  { value: "2025", label: "2025" },
];
const termOptions = [1, 2, 3, 4].map((n) => ({ value: String(n), label: `${n}º bimestre` }));
const classroomOptions = [
  { value: "all", label: "Todas as turmas" },
  { value: "6a", label: "6º ano A" },
  { value: "6b", label: "6º ano B" },
];
const categoryOptions = [
  { value: "all", label: "Todas as categorias" },
  { value: "indisciplina", label: "Indisciplina" },
  { value: "conflito", label: "Conflito entre alunos" },
];

function delta(current: number, previous: number, upIs: StatDelta["upIs"], comparedTo: string): StatDelta {
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
  const [filters, setFilters] = useState<DashboardFilters>({ year: "2026", term: "4", classroom: "all", category: "all" });
  const { data, isFetching } = useQuery({
    queryKey: ["dashboard", filters],
    queryFn: () => fetchDashboard(filters),
    placeholderData: (previous) => previous,
  });

  const update = (key: keyof DashboardFilters) => (e: React.ChangeEvent<HTMLSelectElement>) =>
    setFilters((f) => ({ ...f, [key]: e.target.value }));

  return (
    <>
      <PageHeader
        title="Visão geral"
        description="Indicadores pedagógicos do bimestre. Contam apenas ocorrências registradas, pela data do fato e pela turma da época."
        actions={
          <>
            <Button>
              <Download />
              Exportar
            </Button>
            <Button variant="primary">
              <Plus />
              Nova ocorrência
            </Button>
          </>
        }
      />

      {/* Filtros: uma linha, acima de tudo que eles afetam */}
      <div className="mb-6 grid grid-cols-2 gap-3 sm:flex sm:flex-wrap">
        <Select label="Ano letivo" options={yearOptions} value={filters.year} onChange={update("year")} className="sm:w-32" />
        <Select label="Bimestre" options={termOptions} value={filters.term} onChange={update("term")} className="sm:w-40" />
        <Select label="Turma" options={classroomOptions} value={filters.classroom} onChange={update("classroom")} className="sm:w-48" />
        <Select label="Categoria" options={categoryOptions} value={filters.category} onChange={update("category")} className="sm:w-56" />
      </div>

      {!data ? (
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
            />
            <StatTile
              label="Ocorrências registradas"
              value={format.integer(data.occurrences)}
              icon={ClipboardList}
              delta={delta(data.occurrences, data.occurrencesPrevious, "neutral", `vs ${data.previousTermLabel}`)}
            />
            <StatTile
              label="Alunos com ao menos 1 ocorrência"
              value={format.integer(data.studentsWithOccurrence)}
              icon={UserRound}
              hint={`${format.percent(data.studentsWithOccurrence / data.enrolled, 1)} dos matriculados`}
            />
            <StatTile
              label="Ocorrências concluídas"
              value={format.percent(data.status.concluded / data.occurrences)}
              icon={CheckCircle2}
              hint={`${format.integer(data.status.concluded)} de ${format.integer(data.occurrences)}`}
            />
          </section>

          <section className="mt-4 grid gap-4 xl:grid-cols-3">
            <ChartCard
              className="xl:col-span-2"
              title="Evolução semanal"
              description={`Ocorrências registradas por semana — ${data.termLabel} comparado ao ${data.previousTermLabel}`}
              legend={[
                { label: data.termLabel, color: "var(--series-1)", shape: "line" },
                { label: data.previousTermLabel, color: "var(--series-prev)", shape: "line" },
              ]}
              table={{
                columns: [{ label: "Semana" }, { label: data.termLabel, align: "right" }, { label: data.previousTermLabel, align: "right" }],
                rows: data.weekly.map((w) => [w.week, w.current ?? "—", w.previous]),
              }}
            >
              <LineTrend
                data={data.weekly}
                xKey="week"
                series={[
                  { key: "previous", label: data.previousTermLabel, color: "var(--series-prev)" },
                  { key: "current", label: data.termLabel, color: "var(--series-1)", primary: true },
                ]}
                formatValue={format.integer}
              />
            </ChartCard>

            <ChartCard
              title="Ocorrências por categoria"
              description={data.termLabel}
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
                rows: data.byClassroom.map((c) => [
                  c.classroom,
                  c.shift,
                  c.enrolled,
                  c.occurrences,
                  format.decimal((c.occurrences / c.enrolled) * 100),
                ]),
              }}
              footer="A taxa serve para acompanhar tendências e orientar ações pedagógicas. Não classifica turmas nem alunos."
            >
              <BarList
                data={data.byClassroom
                  .map((c) => ({ label: `${c.classroom} · ${c.shift}`, value: Math.round((c.occurrences / c.enrolled) * 1000) / 10 }))
                  .sort((a, b) => b.value - a.value)}
                seriesLabel="Ocorrências por 100 alunos"
                formatValue={format.decimal}
                labelWidth={150}
              />
            </ChartCard>

            <div className="flex flex-col gap-4">
              <Card>
                <CardHeader title="Situação das ocorrências" description={data.termLabel} />
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
                <CardHeader title="Comunicações aos responsáveis" description="E-mails do bimestre" />
                <CardBody className="space-y-3">
                  <NotificationRow icon={CheckCircle2} tone="text-good-text" label="Enviadas" value={data.notifications.sent} />
                  <NotificationRow icon={AlertTriangle} tone="text-warning-text" label="Pendentes" value={data.notifications.pending} />
                  <NotificationRow icon={XCircle} tone="text-critical-text" label="Com falha" value={data.notifications.failed} action="Reenviar" />
                </CardBody>
              </Card>
            </div>
          </section>

          <Card className="mt-4">
            <CardHeader
              title="Acompanhamentos em aberto"
              description="Ocorrências registradas ou em acompanhamento que aguardam providência"
              actions={<Button size="sm">Ver todas</Button>}
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
                  {data.followUps.map((f) => (
                    <Tr key={f.id}>
                      <Td className="font-medium">{f.student}</Td>
                      <Td className="text-foreground-secondary">{f.classroom}</Td>
                      <Td className="text-foreground-secondary">{f.category}</Td>
                      <Td className="text-foreground-secondary tabular-nums">{format.shortDate(`${f.occurredAt}T12:00:00Z`)}</Td>
                      <Td className="text-foreground-secondary">{f.owner}</Td>
                      <Td>
                        {f.status === "em_acompanhamento" ? (
                          <Badge tone="info">Em acompanhamento</Badge>
                        ) : (
                          <Badge tone="brand">Registrada</Badge>
                        )}
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </Card>
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
}: {
  icon: typeof CheckCircle2;
  tone: string;
  label: string;
  value: number;
  action?: string;
}) {
  return (
    <div className="flex items-center gap-3">
      <Icon className={`size-4 ${tone}`} aria-hidden />
      <span className="flex-1 text-sm text-foreground-secondary">{label}</span>
      {action && value > 0 ? (
        <Button size="sm" variant="ghost" className="h-7 px-2 text-xs">
          {action}
        </Button>
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
