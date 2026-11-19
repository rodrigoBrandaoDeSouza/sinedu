import { api, apiFetch } from "@sinedu/ui";

export type ImportStatus = "Uploaded" | "Validated" | "Processing" | "Completed" | "Cancelled" | "Failed";
export type RowAction = "Pending" | "Empty" | "New" | "Update" | "Unchanged" | "PossibleDuplicate" | "Conflict" | "Rejected";
export type RowDecision = "None" | "Create" | "Update" | "Skip";
export type ImportField =
  | "Code" | "Name" | "Classroom" | "BirthDate" | "Email" | "Address" | "CallNumber"
  | "GuardianName" | "GuardianDocument" | "GuardianPhone" | "GuardianRelationship";

export interface ImportTotals {
  rows: number;
  new: number;
  update: number;
  unchanged: number;
  possibleDuplicate: number;
  conflict: number;
  rejected: number;
  empty: number;
  pending: number;
  newClassrooms: number;
  created: number;
  updated: number;
  skipped: number;
  failed: number;
}

export interface BatchSummary {
  id: string;
  fileName: string;
  status: ImportStatus;
  sheet: string | null;
  createdAt: string;
  completedAt: string | null;
  totals: ImportTotals | null;
}

export interface BatchDetails extends BatchSummary {
  sheets: { name: string; rowCount: number }[];
  headerRow: number;
  headers: (string | null)[];
  sample: (string | null)[][];
  mapping: Partial<Record<ImportField, number>>;
  /** F3-02: mapeamento salvo de uma importação anterior com o mesmo cabeçalho. */
  savedMapping?: boolean;
}

export interface FieldInfo {
  field: ImportField;
  label: string;
  required: boolean;
  hint: string;
}

export interface StudentRef {
  id: string;
  name: string;
  code: string | null;
  birthDate: string | null;
  classroom: string | null;
}

export interface ImportRowView {
  id: string;
  rowNumber: number;
  action: RowAction;
  decision: RowDecision;
  outcome: string;
  errors: string[];
  warnings: string[];
  changes: string[];
  name: string | null;
  code: string | null;
  classroom: string | null;
  birthDate: string | null;
  existing: StudentRef | null;
  candidates: StudentRef[];
}

export const actionLabels: Record<RowAction, string> = {
  Pending: "Pendente",
  Empty: "Linha vazia",
  New: "Novo",
  Update: "Atualização",
  Unchanged: "Sem alteração",
  PossibleDuplicate: "Possível duplicado",
  Conflict: "Conflito",
  Rejected: "Rejeitado",
};

export const statusLabels: Record<ImportStatus, string> = {
  Uploaded: "Aguardando mapeamento",
  Validated: "Em revisão",
  Processing: "Processando",
  Completed: "Concluída",
  Cancelled: "Cancelada",
  Failed: "Falhou",
};

export const importsApi = {
  list: () => api.get<BatchSummary[]>("/api/imports"),
  fields: () => api.get<FieldInfo[]>("/api/imports/fields"),
  get: (id: string, sheet?: string, headerRow?: number) => {
    const p = new URLSearchParams();
    if (sheet) p.set("sheet", sheet);
    if (headerRow) p.set("headerRow", String(headerRow));
    return api.get<BatchDetails>(`/api/imports/${id}${p.size ? `?${p}` : ""}`);
  },
  upload: (file: File) => {
    const body = new FormData();
    body.append("file", file);
    return apiFetch<{ id: string }>("/api/imports", { method: "POST", body });
  },
  map: (id: string, sheet: string, headerRow: number, mapping: Partial<Record<ImportField, number>>) =>
    api.put<ImportTotals>(`/api/imports/${id}/mapping`, { sheet, headerRow, mapping }),
  rows: (id: string, filter: { action?: RowAction; pendingOnly?: boolean; page?: number }) => {
    const p = new URLSearchParams();
    if (filter.action) p.set("action", filter.action);
    if (filter.pendingOnly) p.set("pendingOnly", "true");
    p.set("page", String(filter.page ?? 1));
    return api.get<{ total: number; page: number; pageSize: number; items: ImportRowView[] }>(`/api/imports/${id}/rows?${p}`);
  },
  decide: (id: string, rowId: string, decision: RowDecision, targetStudentId?: string) =>
    api.post(`/api/imports/${id}/rows/${rowId}/decision`, { decision, targetStudentId }),
  skipPending: (id: string) => api.post<{ skipped: number }>(`/api/imports/${id}/skip-pending`),
  confirm: (id: string) => api.post<ImportTotals>(`/api/imports/${id}/confirm`),
  cancel: (id: string) => api.delete(`/api/imports/${id}`),
};
