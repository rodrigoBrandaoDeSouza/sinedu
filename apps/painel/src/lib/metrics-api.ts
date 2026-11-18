import { api } from "@sinedu/ui";

/** Só contagens por escola (P-10, P13). O painel nunca recebe nomes ou dados de alunos. */
export interface TenantMetrics {
  tenantId: string;
  slug: string;
  name: string;
  status: "Provisioning" | "ProvisioningFailed" | "Active" | "Suspended" | "Closing" | "Closed";
  schemaVersion: string | null;
  outdated: boolean;
  collectedAt: string | null;
  activeStudents: number;
  activeClassrooms: number;
  activeMembers: number;
  occurrencesLast30Days: number;
  emailsSentThisMonth: number;
  emailsFailedOpen: number;
  commonStorageBytes: number;
  error: string | null;
}

export interface MetricsOverview {
  schemaVersion: string;
  collectedAt: string | null;
  tenants: TenantMetrics[];
  trend: { day: string; students: number }[];
}

export const metricsApi = {
  overview: () => api.get<MetricsOverview>("/api/metrics"),
  collect: () => api.post<{ tenants: number }>("/api/metrics/collect"),
  migrateTenants: () => api.post<{ updated: number; error: string | null }>("/api/tenants/migrate"),
};

export const formatBytes = (bytes: number) =>
  bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : bytes < 1024 ** 3 ? `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB` : `${(bytes / 1024 ** 3).toFixed(1).replace(".", ",")} GB`;
