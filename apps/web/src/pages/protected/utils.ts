import { ApiError } from "@sinedu/ui";

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString("pt-BR", { timeZone: "America/Sao_Paulo", dateStyle: "short", timeStyle: "short" });

/** Mensagens de campo vindas do ProblemDetails, ou uma mensagem geral. */
export function errorsOf(err: unknown): Record<string, string> {
  if (err instanceof ApiError && Object.keys(err.fieldErrors).length) {
    return Object.fromEntries(Object.entries(err.fieldErrors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v[0] ?? ""]));
  }
  return { form: err instanceof ApiError ? err.message : "Não foi possível salvar." };
}

export const formatSize = (bytes: number) =>
  bytes < 1024 ? `${bytes} B` : bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1).replace(".", ",")} MB`;
