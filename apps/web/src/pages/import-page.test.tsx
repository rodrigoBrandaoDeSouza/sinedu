import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { routes } from "../routes";

const fields = [
  { field: "Code", label: "CGM / código do aluno", required: false, hint: "" },
  { field: "Name", label: "Nome do aluno", required: true, hint: "" },
  { field: "Classroom", label: "Turma", required: true, hint: "" },
];

const totals = { rows: 3, new: 1, update: 0, unchanged: 0, possibleDuplicate: 1, conflict: 1, rejected: 0, empty: 0, pending: 2, newClassrooms: 1, created: 0, updated: 0, skipped: 0, failed: 0 };

function batch(status: string, mapping: Record<string, number>) {
  return {
    id: "b1", fileName: "alunos-2026-anonimizado.xlsx", status, sheet: "Original", createdAt: "2026-11-18T12:00:00Z", completedAt: null,
    totals: status === "Uploaded" ? null : totals, sheets: [{ name: "Original", rowCount: 1033 }], headerRow: 1,
    headers: ["1704", null, "Nome do Aluno", "TURMA - 2026"], sample: [["1", "1750500790", "LÍVIA FREITAS COSTA", "1ª A Manhã"]], mapping,
  };
}

function signedIn(responses: Record<string, unknown>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/me") {
        return Response.json({
          user: { id: "u1", name: "Diretora Fictícia", email: "d@escola.local" }, roles: ["DIRETOR"],
          school: { slug: "escola-teste", name: "Escola de Teste" }, mfa: true, authTime: null, accountUrl: "http://conta.localhost:5175/",
        });
      }
      const key = Object.keys(responses).sort((a, b) => b.length - a.length).find((k) => url.startsWith(k));
      return key ? Response.json(responses[key]) : new Response(null, { status: 404 });
    }),
  );
}

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <ThemeProvider>
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </ThemeProvider>,
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("Importação", () => {
  it("mostra o mapeamento sugerido com exemplos da planilha", async () => {
    signedIn({ "/api/imports/fields": fields, "/api/imports/b1": batch("Uploaded", { Code: 1, Name: 2, Classroom: 3 }) });
    renderAt("/importacoes/b1");

    expect(await screen.findByText("Ligue as colunas da planilha aos campos do Sinedu")).toBeInTheDocument();
    expect((await screen.findAllByText("Coluna 2 (sem título)", { selector: "[role=combobox] span" })).length).toBeGreaterThan(0);
    expect(screen.getByText("Ex.: LÍVIA FREITAS COSTA")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /validar e ver prévia/i })).toBeEnabled();
  });

  it("não deixa validar sem os campos obrigatórios", async () => {
    signedIn({ "/api/imports/fields": fields, "/api/imports/b1": batch("Uploaded", { Code: 1 }) });
    renderAt("/importacoes/b1");

    expect(await screen.findByText(/Falta mapear: Nome do aluno, Turma/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /validar e ver prévia/i })).toBeDisabled();
  });

  it("na prévia, bloqueia a confirmação enquanto houver linhas para revisar", async () => {
    signedIn({
      "/api/imports/b1/rows": {
        total: 1, page: 1, pageSize: 50,
        items: [{
          id: "r1", rowNumber: 785, action: "Conflict", decision: "None", outcome: "None", errors: ["CGM repetido no arquivo (mesmo da linha 237)."], warnings: [],
          changes: [], name: "HELENA FONSECA PEREIRA", code: "1986044612", classroom: "8º C Tarde", birthDate: null, existing: null, candidates: [],
        }],
      },
      "/api/imports/b1": batch("Validated", { Code: 1, Name: 2, Classroom: 3 }),
    });
    renderAt("/importacoes/b1");

    expect(await screen.findByText("CGM repetido no arquivo (mesmo da linha 237).")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirmar importação/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /ignorar linha/i })).toBeInTheDocument();
  });

  it("Professor não tem acesso ao menu de importações", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        Response.json({
          user: { id: "u1", name: "Prof", email: "p@escola.local" }, roles: ["PROFESSOR"],
          school: { slug: "escola-teste", name: "Escola de Teste" }, mfa: false, authTime: null, accountUrl: "http://conta.localhost:5175/",
        }),
      ),
    );
    renderAt("/");

    expect(await screen.findByText(/Olá, Prof/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Importações" })).not.toBeInTheDocument();
  });
});
