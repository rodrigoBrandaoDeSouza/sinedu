import { api, ApiError } from "@sinedu/ui";

export interface Term {
  number: number;
  name: string;
  startsOn: string;
  endsOn: string;
}

export interface FilterOptions {
  years: { id: string; year: number; isActive: boolean; terms: Term[] }[];
  yearId: string | null;
  currentTerm: number | null;
  classrooms: { id: string; name: string; shift: string; gradeOrder: number }[];
  categories: { id: string; name: string }[];
}

export interface Period {
  kind: "term" | "custom";
  label: string;
  start: string;
  end: string;
  previousLabel: string;
  previousStart: string;
  previousEnd: string;
  yearId: string;
  term: number | null;
}

export interface Dashboard {
  period: Period;
  enrolled: number;
  enrolledPrevious: number;
  occurrences: number;
  occurrencesPrevious: number;
  studentsWithOccurrence: number;
  studentsWithOccurrencePrevious: number;
  status: { registered: number; followUp: number; concluded: number };
  notifications: { sent: number; pending: number; failed: number };
  weekly: { week: string; current: number | null; previous: number | null }[];
  byCategory: { label: string; value: number }[];
  byClassroom: { classroomId: string; classroom: string; shift: string; enrolled: number; occurrences: number; ratePer100: number | null }[];
  followUps: { id: string; student: string; classroom: string; category: string; occurredAt: string; status: "Registrada" | "EmAcompanhamento"; owner: string }[];
  method: string;
}

export interface StudentRow {
  studentId: string;
  name: string;
  code: string | null;
  classroom: string;
  occurrences: number;
  open: number;
}

export interface AnalyticsFilter {
  yearId?: string;
  term?: number;
  from?: string;
  to?: string;
  classroomId?: string;
  categoryId?: string;
}

const query = (f: AnalyticsFilter) => {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(f)) if (v !== undefined && v !== "" && v !== null) p.set(k, String(v));
  return p.toString();
};

export const analyticsApi = {
  options: (yearId?: string) => api.get<FilterOptions>(`/api/analytics/options${yearId ? `?yearId=${yearId}` : ""}`),
  dashboard: (f: AnalyticsFilter) => api.get<Dashboard>(`/api/analytics/dashboard?${query(f)}`),
  students: (f: AnalyticsFilter) => api.get<{ period: Period; rows: StudentRow[] }>(`/api/analytics/students?${query(f)}`),
  /** Exportação só com agregados e supressão de contagens pequenas (o servidor aplica). */
  export: async (f: AnalyticsFilter, format: "csv" | "pdf") => {
    const response = await fetch(`/api/analytics/export?format=${format}&${query(f)}`, { credentials: "same-origin" });
    if (!response.ok) throw new ApiError(response.status, undefined, "Não foi possível exportar.");
    const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get("Content-Disposition") ?? "")?.[1];
    const url = URL.createObjectURL(await response.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = name ? decodeURIComponent(name) : `indicadores.${format}`;
    a.hidden = true;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 30_000);
  },
};
