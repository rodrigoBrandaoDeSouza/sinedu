/**
 * Dados do painel gerencial da plataforma.
 *
 * ⚠️ DADOS FICTÍCIOS — as APIs do painel entram nos itens P-02 a P-10 do backlog.
 * O painel nunca recebe dados de alunos: só contagens agregadas por escola.
 */

export type TenantStatus = "ativa" | "provisionando" | "suspensa" | "falha_provisionamento" | "encerrando";

export interface TenantRow {
  id: string;
  name: string;
  slug: string;
  network: string;
  status: TenantStatus;
  plan: string;
  activeStudents: number;
  activeUsers: number;
  storageGb: number;
  schemaVersion: string;
  contractEnds: string | null;
}

export interface BillingAlert {
  id: string;
  kind: "fatura_vencida" | "contrato_vencendo" | "limite_excedido";
  title: string;
  detail: string;
}

export interface Overview {
  tenantsByStatus: Record<TenantStatus, number>;
  activeStudents: number;
  activeStudentsPrevious: number;
  monthlyRecurring: number;
  overdueAmount: number;
  overdueInvoices: number;
  studentsTrend: { month: string; students: number }[];
  billing: { month: string; invoiced: number; received: number }[];
  tenants: TenantRow[];
  alerts: BillingAlert[];
}

const tenants: TenantRow[] = [
  { id: "1", name: "Colégio Estadual Demonstração", slug: "ce-demonstracao", network: "Rede Estadual Fictícia", status: "ativa", plan: "Rede pública · por aluno", activeStudents: 612, activeUsers: 48, storageGb: 3.2, schemaVersion: "0.1.0", contractEnds: "2027-02-28" },
  { id: "2", name: "Escola Municipal Exemplo", slug: "em-exemplo", network: "Rede Municipal Fictícia", status: "ativa", plan: "Rede pública · por aluno", activeStudents: 284, activeUsers: 21, storageGb: 1.1, schemaVersion: "0.1.0", contractEnds: "2026-12-15" },
  { id: "3", name: "Colégio Estadual Modelo", slug: "ce-modelo", network: "Rede Estadual Fictícia", status: "ativa", plan: "Rede pública · por aluno", activeStudents: 941, activeUsers: 63, storageGb: 5.8, schemaVersion: "0.1.0", contractEnds: "2027-02-28" },
  { id: "4", name: "Escola Municipal Teste", slug: "em-teste", network: "Rede Municipal Fictícia", status: "provisionando", plan: "Rede pública · por aluno", activeStudents: 0, activeUsers: 0, storageGb: 0, schemaVersion: "—", contractEnds: "2026-12-15" },
  { id: "5", name: "Colégio Estadual Amostra", slug: "ce-amostra", network: "Rede Estadual Fictícia", status: "suspensa", plan: "Rede pública · por aluno", activeStudents: 455, activeUsers: 0, storageGb: 2.4, schemaVersion: "0.1.0", contractEnds: "2026-10-31" },
  { id: "6", name: "Escola Municipal Protótipo", slug: "em-prototipo", network: "Rede Municipal Fictícia", status: "ativa", plan: "Rede pública · por aluno", activeStudents: 198, activeUsers: 17, storageGb: 0.7, schemaVersion: "0.0.9", contractEnds: "2026-12-15" },
];

export async function fetchOverview(): Promise<Overview> {
  await new Promise((r) => setTimeout(r, 300));

  return {
    tenantsByStatus: { ativa: 4, provisionando: 1, suspensa: 1, falha_provisionamento: 0, encerrando: 0 },
    activeStudents: 2490,
    activeStudentsPrevious: 2310,
    monthlyRecurring: 18_675,
    overdueAmount: 3_412,
    overdueInvoices: 1,
    studentsTrend: [
      { month: "jun", students: 1120 },
      { month: "jul", students: 1180 },
      { month: "ago", students: 1655 },
      { month: "set", students: 1980 },
      { month: "out", students: 2310 },
      { month: "nov", students: 2490 },
    ],
    billing: [
      { month: "jun", invoiced: 8_400, received: 8_400 },
      { month: "jul", invoiced: 8_850, received: 8_850 },
      { month: "ago", invoiced: 12_410, received: 12_410 },
      { month: "set", invoiced: 14_850, received: 14_850 },
      { month: "out", invoiced: 17_325, received: 13_913 },
      { month: "nov", invoiced: 18_675, received: 0 },
    ],
    tenants,
    alerts: [
      { id: "a1", kind: "fatura_vencida", title: "Fatura de outubro vencida", detail: "Colégio Estadual Amostra · R$ 3.412 · venceu em 10/11" },
      { id: "a2", kind: "contrato_vencendo", title: "Contrato vence em 27 dias", detail: "Rede Municipal Fictícia · 3 escolas cobertas" },
      { id: "a3", kind: "limite_excedido", title: "Migração pendente", detail: "Escola Municipal Protótipo está na versão 0.0.9" },
    ],
  };
}
