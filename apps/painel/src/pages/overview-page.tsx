import { useQuery } from "@tanstack/react-query";
import {
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  ChartCard,
  ColumnGroups,
  format,
  LineTrend,
  PageHeader,
  StatTile,
} from "@sinedu/ui";
import { AlertTriangle, Building2, CalendarClock, CircleDollarSign, DatabaseZap, GraduationCap, Plus, Receipt } from "lucide-react";
import { Link } from "react-router";
import { fetchOverview, type BillingAlert } from "../data/overview";
import { TenantTable } from "../components/tenant-table";

const alertIcons: Record<BillingAlert["kind"], typeof AlertTriangle> = {
  fatura_vencida: Receipt,
  contrato_vencendo: CalendarClock,
  limite_excedido: DatabaseZap,
};

export function OverviewPage() {
  const { data } = useQuery({ queryKey: ["overview"], queryFn: fetchOverview });

  return (
    <>
      <PageHeader
        title="Visão geral"
        eyebrow={
          <Badge tone="warning" icon>
            Dados fictícios — métricas e financeiro reais nos itens P-06 a P-10
          </Badge>
        }
        description="Escolas, uso e financeiro da plataforma. O painel nunca exibe dados de alunos — só contagens."
        actions={
          <Button variant="primary">
            <Plus />
            Nova escola
          </Button>
        }
      />

      {!data ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-busy="true" aria-label="Carregando">
          {Array.from({ length: 4 }, (_, i) => (
            <div key={i} className="h-[132px] animate-pulse rounded-(--radius-card) border border-border bg-surface" />
          ))}
        </div>
      ) : (
        <>
          <section aria-label="Resumo" className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatTile
              label="Escolas ativas"
              value={data.tenantsByStatus.ativa}
              icon={Building2}
              hint={`${data.tenantsByStatus.provisionando} provisionando · ${data.tenantsByStatus.suspensa} suspensa`}
            />
            <StatTile
              label="Alunos ativos"
              value={format.integer(data.activeStudents)}
              icon={GraduationCap}
              delta={{
                label: `+${format.percent((data.activeStudents - data.activeStudentsPrevious) / data.activeStudentsPrevious, 1)}`,
                direction: "up",
                upIs: "good",
                comparedTo: "vs outubro",
              }}
            />
            <StatTile
              label="Receita recorrente mensal"
              value={format.currency(data.monthlyRecurring)}
              icon={CircleDollarSign}
              hint="Contratos vigentes"
            />
            <StatTile
              label="Em atraso"
              value={format.currency(data.overdueAmount)}
              icon={Receipt}
              hint={
                <span className="inline-flex items-center gap-1 text-critical-text">
                  <AlertTriangle className="size-3.5" aria-hidden />
                  {data.overdueInvoices} fatura vencida
                </span>
              }
            />
          </section>

          <section className="mt-4 grid gap-4 xl:grid-cols-3">
            <ChartCard
              title="Alunos ativos na plataforma"
              description="Soma das escolas ativas, fim de cada mês"
              table={{
                columns: [{ label: "Mês" }, { label: "Alunos ativos", align: "right" }],
                rows: data.studentsTrend.map((p) => [p.month, format.integer(p.students)]),
              }}
            >
              <LineTrend
                data={data.studentsTrend}
                xKey="month"
                series={[{ key: "students", label: "Alunos ativos", color: "var(--series-1)", primary: true }]}
                formatValue={format.auto}
              />
            </ChartCard>

            <ChartCard
              className="xl:col-span-2"
              title="Faturado x recebido"
              description="Por mês de competência"
              legend={[
                { label: "Faturado", color: "var(--series-1)" },
                { label: "Recebido", color: "var(--series-3)" },
              ]}
              table={{
                columns: [{ label: "Mês" }, { label: "Faturado", align: "right" }, { label: "Recebido", align: "right" }],
                rows: data.billing.map((b) => [b.month, format.currency(b.invoiced), format.currency(b.received)]),
              }}
            >
              <ColumnGroups
                data={data.billing}
                xKey="month"
                series={[
                  { key: "invoiced", label: "Faturado", color: "var(--series-1)" },
                  { key: "received", label: "Recebido", color: "var(--series-3)" },
                ]}
                formatValue={format.currency}
                formatAxis={format.currencyCompact}
              />
            </ChartCard>
          </section>

          <section className="mt-4 grid gap-4 xl:grid-cols-3">
            <Card className="xl:col-span-2">
              <CardHeader
                title="Escolas"
                description="Situação, plano e uso de cada escola"
                actions={
                  <Link to="/escolas" className="text-sm font-medium text-primary hover:underline">
                    Ver todas
                  </Link>
                }
              />
              <div className="mt-4">
                <TenantTable tenants={data.tenants.slice(0, 5)} compact />
              </div>
            </Card>

            <Card>
              <CardHeader title="Alertas" description="Cobrança, contratos e operação" />
              <CardBody>
                <ul className="space-y-3">
                  {data.alerts.map((a) => {
                    const Icon = alertIcons[a.kind];
                    return (
                      <li key={a.id} className="flex gap-3 rounded-xl border border-border bg-surface-raised p-3">
                        <span className="grid size-8 shrink-0 place-items-center rounded-lg bg-warning-soft text-warning-text">
                          <Icon className="size-4" aria-hidden />
                        </span>
                        <div className="min-w-0">
                          <p className="text-sm font-medium text-foreground">{a.title}</p>
                          <p className="mt-0.5 text-xs text-muted-foreground">{a.detail}</p>
                        </div>
                      </li>
                    );
                  })}
                </ul>
                <div className="mt-4 flex items-center gap-2 text-xs text-muted-foreground">
                  <Badge tone="outline">Manual</Badge>A suspensão de escolas é sempre uma decisão manual.
                </div>
              </CardBody>
            </Card>
          </section>
        </>
      )}
    </>
  );
}
