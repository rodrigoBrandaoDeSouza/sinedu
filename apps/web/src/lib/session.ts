import { createContext, useContext } from "react";

export type SchoolRole = "DIRETOR" | "PEDAGOGO" | "ADMIN_TECNICO" | "PROFESSOR" | "CONSULTA" | "SUPORTE_PLATAFORMA";

export interface Me {
  user: { id: string; name: string; email: string };
  roles: SchoolRole[];
  school: { slug: string; name: string };
  mfa: boolean;
  authTime: string | null;
  accountUrl: string;
}

export const roleLabels: Record<SchoolRole, string> = {
  DIRETOR: "Direção",
  PEDAGOGO: "Pedagogia",
  ADMIN_TECNICO: "Administração técnica",
  PROFESSOR: "Professor(a)",
  CONSULTA: "Consulta",
  SUPORTE_PLATAFORMA: "Suporte da plataforma",
};

export const SessionContext = createContext<Me | null>(null);

/** Vai para o login único (OIDC) e volta para a mesma página. */
export function redirectToLogin(stepUp = false) {
  const returnUrl = window.location.pathname + window.location.search;
  window.location.assign(`/auth/login?returnUrl=${encodeURIComponent(returnUrl)}${stepUp ? "&stepUp=true" : ""}`);
}

export function useSession(): Me {
  const me = useContext(SessionContext);
  if (!me) throw new Error("useSession fora do SessionProvider.");
  return me;
}

export function useHasRole(...roles: SchoolRole[]) {
  return useSession().roles.some((r) => roles.includes(r));
}
