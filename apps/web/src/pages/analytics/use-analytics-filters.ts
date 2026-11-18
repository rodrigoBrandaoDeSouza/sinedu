import { useQuery } from "@tanstack/react-query";
import { useSearchParams } from "react-router";
import { analyticsApi, type AnalyticsFilter, type FilterOptions } from "../../lib/analytics-api";

/** Filtros dos indicadores na URL (compartilháveis): ano, bimestre ou período, turma e categoria. */
export function useAnalyticsFilters(): { filter: AnalyticsFilter; options: FilterOptions | undefined; set: (changes: Record<string, string>) => void; ready: boolean } {
  const [params, setParams] = useSearchParams();
  const yearId = params.get("ano") || undefined;
  const options = useQuery({ queryKey: ["analytics-options", yearId], queryFn: () => analyticsApi.options(yearId) });
  const custom = params.get("periodo") === "personalizado";
  const filter: AnalyticsFilter = {
    yearId: yearId ?? options.data?.yearId ?? undefined,
    term: custom ? undefined : Number(params.get("bimestre")) || options.data?.currentTerm || undefined,
    from: custom ? params.get("de") || undefined : undefined,
    to: custom ? params.get("ate") || undefined : undefined,
    classroomId: params.get("turma") || undefined,
    categoryId: params.get("categoria") || undefined,
  };

  const set = (changes: Record<string, string>) => {
    const next = new URLSearchParams(params);
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value);
      else next.delete(key);
      if (key === "ano") next.delete("turma");
    }
    setParams(next, { replace: true });
  };

  const ready = options.isSuccess && !!filter.yearId && (custom ? !!filter.from && !!filter.to : !!filter.term);
  return { filter, options: options.data, set, ready };
}
