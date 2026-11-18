import { Input, Select } from "@sinedu/ui";
import { useSearchParams } from "react-router";
import type { AnalyticsFilter, FilterOptions } from "../../lib/analytics-api";

export function FiltersBar({ filter, options, set }: { filter: AnalyticsFilter; options: FilterOptions | undefined; set: (changes: Record<string, string>) => void }) {
  const [params] = useSearchParams();
  const custom = params.get("periodo") === "personalizado";
  const year = options?.years.find((y) => y.id === filter.yearId);

  return (
    <div className="mb-6 grid grid-cols-2 gap-3 sm:flex sm:flex-wrap sm:items-end">
      <Select
        label="Ano letivo"
        options={(options?.years ?? []).map((y) => ({ value: y.id, label: String(y.year) }))}
        value={filter.yearId ?? ""}
        onChange={(e) => set({ ano: e.target.value })}
        className="sm:w-32"
      />
      <Select
        label="Período"
        options={[...(year?.terms ?? []).map((t) => ({ value: String(t.number), label: t.name })), { value: "personalizado", label: "Período personalizado" }]}
        value={custom ? "personalizado" : String(filter.term ?? "")}
        onChange={(e) => {
          if (e.target.value === "personalizado") set({ periodo: "personalizado" });
          else set({ periodo: "", bimestre: e.target.value });
        }}
        className="sm:w-48"
      />
      {custom ? (
        <>
          <Input label="De" type="date" value={filter.from ?? ""} onChange={(e) => set({ de: e.target.value })} className="sm:w-40" />
          <Input label="Até" type="date" value={filter.to ?? ""} onChange={(e) => set({ ate: e.target.value })} className="sm:w-40" />
        </>
      ) : null}
      <Select
        label="Turma"
        options={[{ value: "", label: "Todas as turmas" }, ...(options?.classrooms ?? []).map((c) => ({ value: c.id, label: c.name }))]}
        value={filter.classroomId ?? ""}
        onChange={(e) => set({ turma: e.target.value })}
        className="sm:w-48"
      />
      <Select
        label="Categoria"
        options={[{ value: "", label: "Todas as categorias" }, ...(options?.categories ?? []).map((c) => ({ value: c.id, label: c.name }))]}
        value={filter.categoryId ?? ""}
        onChange={(e) => set({ categoria: e.target.value })}
        className="sm:w-56"
      />
    </div>
  );
}
