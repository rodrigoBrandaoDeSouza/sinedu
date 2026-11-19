import { api } from "@sinedu/ui";

export type PendingStage = "mfa" | "enroll" | null;

export interface SessionInfo {
  authenticated: boolean;
  pendingStage?: PendingStage;
  user?: { id: string; email: string; fullName: string; mfaEnabled: boolean; kind: string };
  schools?: { slug: string; name: string; url: string; supportUntil?: string | null }[];
  panel?: string | null;
}

export type AuthStatus = "signed_in" | "mfa_required" | "mfa_enrollment_required";

export const getSession = () => api.get<SessionInfo>("/api/auth/session");

/**
 * Para onde ir depois de entrar. Só aceita o próprio host (ex.: /connect/authorize?... do OIDC)
 * para não virar um redirecionador aberto.
 */
export function safeReturnUrl(value: string | null): string {
  if (!value || !value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) return "/";
  return value;
}
