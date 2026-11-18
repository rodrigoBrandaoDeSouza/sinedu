import { useQuery } from "@tanstack/react-query";
import { Badge, Card, EmptyState, PageHeader, Select } from "@sinedu/ui";
import { BookOpenCheck, ChevronRight, Users } from "lucide-react";
import { useMemo, useState } from "react";
import { Link } from "react-router";
import { schoolApi, shiftLabels, stageLabels, type Classroom, type Shift, type Stage } from "../lib/school-api";

const shiftTone: Record<Shift, "info" | "warning" | "brand" | "neutral"> = { Manha: "info", Tarde: "warning", Noite: "brand", Integral: "neutral" };

export function ClassroomsPage() {
  const [shift, setShift] = useState<"all" | Shift>("all");
  const classrooms = useQuery({ queryKey: ["classrooms"], queryFn: () => schoolApi.classrooms() });
  const counts = useQuery({ queryKey: ["classroom-counts"], queryFn: schoolApi.classroomCounts });
  const years = useQuery({ queryKey: ["years"], queryFn: schoolApi.years });

  const countOf = useMemo(() => new Map(counts.data?.map((c) => [c.classroomId, c.students]) ?? []), [counts.data]);
  const activeYear = years.data?.find((y) => y.status === "Active");
  const filtered = (classrooms.data ?? []).filter((c) => shift === "all" || c.shift === shift);
  const byStage = groupBy(filtered, (c) => c.stage);
  const total = filtered.reduce((sum, c) => sum + (countOf.get(c.id) ?? 0), 0);

  return (
    <>
      <PageHeader
        title="Turmas"
        description={
          activeYear
            ? `Ano letivo ${activeYear.year} · ${filtered.length} turmas · ${total.toLocaleString("pt-BR")} alunos com matrícula ativa`
            : "Nenhum ano letivo ativo."
        }
        actions={
          <Select
            label="Turno"
            hideLabel
            className="w-40"
            value={shift}
            onChange={(e) => setShift(e.target.value as typeof shift)}
            options={[{ value: "all", label: "Todos os turnos" }, ...(["Manha", "Tarde", "Noite"] as Shift[]).map((s) => ({ value: s, label: shiftLabels[s] }))]}
          />
        }
      />

      {classrooms.isLoading ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          {Array.from({ length: 8 }, (_, i) => (
            <div key={i} className="h-[104px] animate-pulse rounded-(--radius-card) border border-border bg-surface" />
          ))}
        </div>
      ) : filtered.length === 0 ? (
        <EmptyState icon={BookOpenCheck} title="Nenhuma turma" description="Turmas aparecem aqui quando forem cadastradas ou importadas — ou quando a direção vincular você a uma turma." />
      ) : (
        <div className="space-y-8">
          {[...byStage.entries()].map(([stage, items]) => (
            <section key={stage}>
              <h2 className="mb-3 text-sm font-semibold text-foreground-secondary">{stageLabels[stage as Stage]}</h2>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
                {items.map((c) => (
                  <ClassroomCard key={c.id} classroom={c} students={countOf.get(c.id) ?? 0} />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </>
  );
}

function ClassroomCard({ classroom: c, students }: { classroom: Classroom; students: number }) {
  return (
    <Link to={`/turmas/${c.id}`} className="group">
      <Card className="flex h-full flex-col gap-3 p-4 transition-colors group-hover:border-primary/40">
        <div className="flex items-start justify-between gap-2">
          <div>
            <p className="text-lg font-semibold tracking-tight text-foreground">
              {c.name.split(" ").slice(0, 2).join(" ")}
            </p>
            <p className="text-xs text-muted-foreground">{c.gradeName}</p>
          </div>
          <Badge tone={shiftTone[c.shift]}>{shiftLabels[c.shift]}</Badge>
        </div>
        <div className="mt-auto flex items-center justify-between text-sm">
          <span className="inline-flex items-center gap-1.5 text-foreground-secondary">
            <Users className="size-4 text-muted-foreground" aria-hidden />
            <span className="font-semibold text-foreground tabular-nums">{students}</span> alunos
          </span>
          <ChevronRight className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" aria-hidden />
        </div>
        {c.teachers.length ? <p className="truncate text-xs text-muted-foreground">{c.teachers.map((t) => t.name).join(", ")}</p> : null}
      </Card>
    </Link>
  );
}

function groupBy<T, K>(items: T[], key: (item: T) => K) {
  const map = new Map<K, T[]>();
  for (const item of items) map.set(key(item), [...(map.get(key(item)) ?? []), item]);
  return map;
}
