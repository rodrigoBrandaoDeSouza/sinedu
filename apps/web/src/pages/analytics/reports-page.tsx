import { useQuery } from "@tanstack/react-query";
import { Alert, Badge, Card, EmptyState, PageHeader, Table, Td, Th, Tr } from "@sinedu/ui";
import { Lock, Users } from "lucide-react";
import { useNavigate } from "react-router";
import { analyticsApi } from "../../lib/analytics-api";
import { ExportMenu } from "./export-menu";
import { FiltersBar } from "./filters";
import { useAnalyticsFilters } from "./use-analytics-filters";

/**
 * Relatórios: ocorrências por aluno (consulta restrita à Direção e Pedagogia). Ordem alfabética — não é ranking.
 * Esta lista nunca entra na exportação; a exportação tem só agregados.
 */
export function ReportsPage() {
  const navigate = useNavigate();
  const { filter, options, set, ready } = useAnalyticsFilters();
  const result = useQuery({ queryKey: ["analytics-students", filter], queryFn: () => analyticsApi.students(filter), enabled: ready });

  return (
    <>
      <PageHeader
        title="Relatórios"
        description="Ocorrências por aluno no período, para acompanhamento pedagógico."
        actions={ready ? <ExportMenu filter={filter} /> : null}
      />
      <FiltersBar filter={filter} options={options} set={set} />

      <Alert tone="info" className="mb-4" title="Consulta restrita">
        <span className="inline-flex items-center gap-1">
          <Lock className="size-3.5" aria-hidden />
          Lista em ordem alfabética, sem pontuação nem classificação de alunos. Não é exportada: a exportação traz só números agregados.
        </span>
      </Alert>

      {result.data?.rows.length === 0 ? (
        <EmptyState icon={Users} title="Nenhum aluno com ocorrência no período" description="Ajuste o período, a turma ou a categoria." />
      ) : (
        <Card>
          <Table>
            <thead>
              <tr>
                <Th>Aluno</Th>
                <Th>Turma da última ocorrência</Th>
                <Th className="text-right">Ocorrências</Th>
                <Th>Em aberto</Th>
              </tr>
            </thead>
            <tbody>
              {result.data?.rows.map((r) => (
                <Tr key={r.studentId} className="cursor-pointer" onClick={() => navigate(`/alunos/${r.studentId}`)}>
                  <Td>
                    <p className="font-medium text-foreground">{r.name}</p>
                    {r.code ? <p className="font-mono text-xs text-muted-foreground">CGM {r.code}</p> : null}
                  </Td>
                  <Td className="text-foreground-secondary">{r.classroom}</Td>
                  <Td className="text-right tabular-nums">{r.occurrences}</Td>
                  <Td>
                    {r.open > 0 ? (
                      <Badge tone="warning" icon>
                        {r.open} em aberto
                      </Badge>
                    ) : (
                      <Badge tone="good" icon>
                        Nenhuma
                      </Badge>
                    )}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}
    </>
  );
}
