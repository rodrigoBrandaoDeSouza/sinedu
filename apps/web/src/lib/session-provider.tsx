import { useQuery } from "@tanstack/react-query";
import { ApiError, apiFetch } from "@sinedu/ui";
import type { ReactNode } from "react";
import { redirectToLogin, SessionContext, type Me } from "./session";

/** Carrega a sessão; sem sessão, redireciona para o login único. */
export function SessionProvider({ children, fallback }: { children: ReactNode; fallback: ReactNode }) {
  const { data, error } = useQuery({
    queryKey: ["me"],
    queryFn: () => apiFetch<Me>("/api/me"),
    retry: false,
    staleTime: 5 * 60_000,
  });

  if (error instanceof ApiError && error.status === 401) {
    redirectToLogin();
    return fallback;
  }

  if (!data) return fallback;
  return <SessionContext.Provider value={data}>{children}</SessionContext.Provider>;
}
