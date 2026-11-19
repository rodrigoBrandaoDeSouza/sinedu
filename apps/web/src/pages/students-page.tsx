import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Button, Card, EmptyState, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { ChevronLeft, ChevronRight, GraduationCap, Search } from "lucide-react";
import { useDeferredValue } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { schoolApi } from "../lib/school-api";

export function StudentsPage() {
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const query = params.get("q") ?? "";
  const classroomId = params.get("turma") ?? "";
  const page = Number(params.get("pagina") ?? 1);
  const deferredQuery = useDeferredValue(query.trim());

  const classrooms = useQuery({ queryKey: ["classrooms"], queryFn: () => schoolApi.classrooms() });
  const students = useQuery({
    queryKey: ["students", { query: deferredQuery, classroomId, page }],
    queryFn: () => schoolApi.students({ query: deferredQuery.length >= 2 ? deferredQuery : undefined, classroomId: classroomId || undefined, page }),
    placeholderData: keepPreviousData,
  });

  const update = (next: Record<string, string | null>) => {
    const p = new URLSearchParams(params);
    for (const [k, v] of Object.entries(next)) {
      if (v) p.set(k, v);
      else p.delete(k);
    }
    setParams(p, { replace: true });
  };

  const data = students.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <>
      <PageHeader title="Alunos" description={data ? `${data.total.toLocaleString("pt-BR")} alunos com matrícula ativa no seu acesso` : "Carregando…"} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="relative min-w-64 flex-1 sm:max-w-md">
          <span className="sr-only">Buscar por nome ou CGM</span>
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <input
            type="search"
            value={query}
            onChange={(e) => update({ q: e.target.value || null, pagina: null })}
            placeholder="Buscar por nome ou CGM"
            autoComplete="off"
            className="h-10 w-full rounded-lg border border-border-strong bg-surface-raised pl-9 pr-3 text-sm placeholder:text-muted-foreground transition-colors hover:border-foreground/30"
          />
        </label>
        <Select
          label="Turma"
          hideLabel
          className="w-52"
          value={classroomId}
          onChange={(e) => update({ turma: e.target.value || null, pagina: null })}
          options={[{ value: "", label: "Todas as turmas" }, ...(classrooms.data ?? []).map((c) => ({ value: c.id, label: c.name }))]}
        />
      </div>

      {data && data.total === 0 ? (
        <EmptyState
          icon={GraduationCap}
          title={deferredQuery ? "Nenhum aluno encontrado" : "Nenhum aluno no seu acesso"}
          description={deferredQuery ? "Confira a grafia ou busque pelo CGM." : "Alunos aparecem aqui quando estiverem matriculados em turmas que você pode ver."}
        />
      ) : (
        <Card className={students.isFetching && students.isPlaceholderData ? "opacity-60 transition-opacity" : "transition-opacity"}>
          <Table>
            <thead>
              <tr>
                <Th>Nome</Th>
                <Th>CGM</Th>
                <Th>Turma</Th>
                <Th align="right">Nº</Th>
                <Th align="right">Idade</Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.map((s) => (
                <Tr key={s.id} className="cursor-pointer" onClick={() => navigate(`/alunos/${s.id}`)}>
                  <Td>
                    <Link to={`/alunos/${s.id}`} className="font-medium hover:text-primary" onClick={(e) => e.stopPropagation()}>
                      {s.name}
                    </Link>
                    {s.civilName ? <span className="ml-2 text-xs text-muted-foreground">(nome social)</span> : null}
                  </Td>
                  <Td className="font-mono text-xs text-foreground-secondary">{s.institutionalCode ?? "—"}</Td>
                  <Td className="text-foreground-secondary">{s.classroom?.name ?? "—"}</Td>
                  <Td align="right" className="text-muted-foreground">{s.callNumber ?? "—"}</Td>
                  <Td align="right">{s.age ?? "—"}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
          {data && pages > 1 ? (
            <div className="flex items-center justify-between border-t border-border px-5 py-3 text-sm text-muted-foreground">
              <span>
                Página {data.page} de {pages}
              </span>
              <div className="flex gap-2">
                <Button size="sm" disabled={page <= 1} onClick={() => update({ pagina: String(page - 1) })}>
                  <ChevronLeft />
                  Anterior
                </Button>
                <Button size="sm" disabled={page >= pages} onClick={() => update({ pagina: String(page + 1) })}>
                  Próxima
                  <ChevronRight />
                </Button>
              </div>
            </div>
          ) : null}
        </Card>
      )}
    </>
  );
}
