import { createContext, useContext } from "react";

export interface PanelMe {
  id: string;
  name: string;
  email: string;
  roles: ("SUPER_ADMIN" | "SUPORTE")[];
}

export const SessionContext = createContext<PanelMe | null>(null);

export function redirectToLogin() {
  const returnUrl = window.location.pathname + window.location.search;
  window.location.assign(`/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`);
}

export function useSession(): PanelMe {
  const me = useContext(SessionContext);
  if (!me) throw new Error("useSession fora do SessionProvider.");
  return me;
}

export const useIsSuperAdmin = () => useSession().roles.includes("SUPER_ADMIN");
