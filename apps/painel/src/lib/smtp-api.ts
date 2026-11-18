import { api } from "@sinedu/ui";

export interface SmtpSettings {
  configured: boolean;
  host: string | null;
  port: number;
  useTls: boolean;
  username: string | null;
  hasPassword: boolean;
  fromAddress: string | null;
  fromName: string | null;
}

export interface SmtpInput {
  host: string;
  port: number;
  useTls: boolean;
  username: string | null;
  /** Vazio = manter a senha atual (ela nunca volta da API). */
  password: string | null;
  fromAddress: string;
  fromName: string | null;
}

export interface EmailTestResult {
  ok: boolean;
  school: string;
  source: string;
  server: string;
  tls: boolean;
  from: string;
  elapsedMs: number;
  error: string | null;
}

export const smtpApi = {
  get: (tenantId: string) => api.get<SmtpSettings>(`/api/tenants/${tenantId}/smtp`),
  save: (tenantId: string, body: SmtpInput) => api.put(`/api/tenants/${tenantId}/smtp`, body),
  remove: (tenantId: string) => api.delete(`/api/tenants/${tenantId}/smtp`),
  test: (body: { tenantId: string | null; to: string; smtp?: SmtpInput }) => api.post<EmailTestResult>("/api/tenants/email-test", body),
};
