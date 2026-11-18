/** Erro de API no formato ProblemDetails (RFC 9457). */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string | undefined,
    message: string,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

interface ProblemBody {
  title?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/**
 * fetch para a API do próprio host: cookies de sessão, JSON e erros padronizados.
 * Nunca envia dados para outros domínios.
 */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (init.body && !(init.body instanceof FormData)) headers.set("Content-Type", "application/json");

  const response = await fetch(path, { ...init, headers, credentials: "same-origin" });
  if (response.ok) {
    return (response.status === 204 ? undefined : await response.json()) as T;
  }

  let problem: ProblemBody = {};
  try {
    problem = (await response.json()) as ProblemBody;
  } catch {
    // corpo vazio
  }

  const fallback =
    response.status === 403 ? "Você não tem permissão para esta ação." : response.status === 404 ? "Não encontrado." : "Algo deu errado. Tente novamente.";
  const fieldErrors = problem.errors ?? {};
  const firstFieldError = Object.values(fieldErrors)[0]?.[0];
  throw new ApiError(response.status, problem.code, problem.title && !problem.errors ? problem.title : (firstFieldError ?? problem.title ?? fallback), fieldErrors);
}

export const api = {
  get: <T>(path: string) => apiFetch<T>(path),
  post: <T>(path: string, body?: unknown) => apiFetch<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }),
  delete: <T>(path: string) => apiFetch<T>(path, { method: "DELETE" }),
};

/** Envia um formulário POST real (navegação), usado para logout com redirecionamento ao login único. */
export function postNavigate(action: string) {
  const form = document.createElement("form");
  form.method = "POST";
  form.action = action;
  document.body.appendChild(form);
  form.submit();
}
