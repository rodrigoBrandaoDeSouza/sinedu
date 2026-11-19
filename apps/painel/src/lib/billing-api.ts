import { api, ApiError } from "@sinedu/ui";

export interface Tier {
  upToStudents: number | null;
  monthlyPrice: number;
}

export interface Plan {
  id: string;
  name: string;
  isActive: boolean;
  contracts: number;
  tiers: Tier[];
}

export type ContractStatus = "Active" | "Ended" | "Cancelled";

export interface Contract {
  id: string;
  tenant: { id: string; name: string; slug: string | null };
  plan: { id: string; name: string };
  payerName: string;
  payerDocument: string | null;
  payerContactName: string | null;
  payerEmail: string | null;
  payerPhone: string | null;
  startsOn: string;
  endsOn: string | null;
  dueDay: number;
  status: ContractStatus;
  notes: string | null;
}

export interface ContractInput {
  tenantId: string;
  planId: string;
  payerName: string;
  payerDocument: string | null;
  payerContactName: string | null;
  payerEmail: string | null;
  payerPhone: string | null;
  startsOn: string;
  endsOn: string | null;
  dueDay: number;
  notes: string | null;
  status: ContractStatus;
}

export type InvoiceStatus = "Open" | "Overdue" | "Paid" | "Cancelled";

export interface Invoice {
  id: string;
  contractId: string;
  school: string;
  payer: string;
  referenceMonth: string;
  students: number;
  tierLabel: string;
  amount: number;
  dueOn: string;
  status: InvoiceStatus;
  invoiceNumber: string | null;
  issuedOn: string | null;
  paidOn: string | null;
  paidAmount: number | null;
  notes: string | null;
}

export interface BillingAlert {
  kind: "fatura_vencida" | "contrato_vencendo" | "contrato_vencido" | "sem_contrato" | "limite_excedido";
  tenantId: string;
  school: string;
  message: string;
  contractId: string | null;
  invoiceId: string | null;
}

export const billingApi = {
  plans: () => api.get<Plan[]>("/api/billing/plans"),
  savePlan: (id: string | null, body: { name: string; tiers: Tier[]; isActive: boolean }) =>
    id ? api.put(`/api/billing/plans/${id}`, body) : api.post<{ id: string }>("/api/billing/plans", body),
  contracts: () => api.get<Contract[]>("/api/billing/contracts"),
  saveContract: (id: string | null, body: ContractInput) =>
    id ? api.put(`/api/billing/contracts/${id}`, body) : api.post<{ id: string }>("/api/billing/contracts", body),
  invoices: (filter: { month?: string; status?: string; overdue?: boolean }) => {
    const q = new URLSearchParams();
    if (filter.month) q.set("month", filter.month);
    if (filter.status) q.set("status", filter.status);
    if (filter.overdue) q.set("overdue", "true");
    return api.get<Invoice[]>(`/api/billing/invoices?${q}`);
  },
  generate: (month: string) => api.post<{ month: string; created: number; alreadyExisted: number; skippedWithoutCount: string[] }>("/api/billing/invoices/generate", { month }),
  pay: (id: string, body: { paidOn: string; paidAmount: number; invoiceNumber: string | null; issuedOn: string | null; notes: string | null }) =>
    api.post(`/api/billing/invoices/${id}/pay`, body),
  fiscalNote: (id: string, body: { invoiceNumber: string | null; issuedOn: string | null }) => api.put(`/api/billing/invoices/${id}/fiscal-note`, body),
  cancel: (id: string, reason: string) => api.post(`/api/billing/invoices/${id}/cancel`, { reason }),
  alerts: () => api.get<BillingAlert[]>("/api/billing/alerts"),
};

export const alertLabels: Record<BillingAlert["kind"], string> = {
  fatura_vencida: "Fatura vencida",
  contrato_vencendo: "Contrato vencendo",
  contrato_vencido: "Contrato vencido",
  sem_contrato: "Sem contrato",
  limite_excedido: "Acima do plano",
};

/** "2026-11-19" → "19/11/2026" (datas sem fuso, como vêm da API). */
export const dateBr = (iso: string | null) => (iso ? iso.slice(0, 10).split("-").reverse().join("/") : "—");

/** "2026-11" → "nov/2026" */
export const monthBr = (ym: string) => {
  const [y, m] = ym.split("-").map(Number);
  return new Date(y!, m! - 1, 1).toLocaleDateString("pt-BR", { month: "short", year: "numeric" }).replace(". de ", "/").replace(" de ", "/");
};

export const currentMonth = () => {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
};

export const tierLabel = (tiers: Tier[], index: number) => {
  const previous = index > 0 ? tiers[index - 1]!.upToStudents ?? 0 : 0;
  const t = tiers[index]!;
  return t.upToStudents == null ? `Acima de ${previous} alunos` : `${previous + 1} a ${t.upToStudents} alunos`;
};

/** Erros de validação da API por campo (camelCase), ou "form" com a mensagem geral. */
export const fieldErrors = (err: unknown): Record<string, string> => {
  if (err instanceof ApiError && Object.keys(err.fieldErrors).length) {
    return Object.fromEntries(Object.entries(err.fieldErrors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v[0] ?? ""]));
  }
  return { form: err instanceof ApiError ? err.message : "Não foi possível salvar." };
};
