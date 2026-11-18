import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SchoolRole } from "../lib/session";
import { routes } from "../routes";

function signedInAs(roles: SchoolRole[], extra: Record<string, unknown> = {}) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/me") {
        return Response.json({
          user: { id: "u1", name: "Pessoa Fictícia", email: "pessoa@escola.local" },
          roles,
          school: { slug: "escola-demo", name: "Colégio Estadual Demonstração" },
          mfa: true,
          authTime: new Date().toISOString(),
          accountUrl: "http://conta.localhost:5175/",
        });
      }
      const key = Object.keys(extra).sort((a, b) => b.length - a.length).find((k) => url.startsWith(k));
      const body = key ? extra[key] : undefined;
      return body ? Response.json(body) : new Response(null, { status: 404 });
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

// Indicadores FICTÍCIOS para o teste (nenhum dado real).
const analytics = {
  "/api/analytics/options": {
    years: [{ id: "y1", year: 2026, isActive: true, terms: [{ number: 4, name: "4º bimestre", startsOn: "2026-10-01", endsOn: "2026-12-18" }] }],
    yearId: "y1", currentTerm: 4, classrooms: [{ id: "c1", name: "6º A Manhã", shift: "Manhã", gradeOrder: 6 }], categories: [{ id: "k1", name: "Indisciplina" }],
  },
  "/api/analytics/dashboard": {
    period: { kind: "term", label: "4º bimestre", start: "2026-10-01", end: "2026-12-18", previousLabel: "3º bimestre", previousStart: "2026-07-20", previousEnd: "2026-09-30", yearId: "y1", term: 4 },
    enrolled: 32, enrolledPrevious: 31, occurrences: 7, occurrencesPrevious: 5, studentsWithOccurrence: 4, studentsWithOccurrencePrevious: 3,
    status: { registered: 2, followUp: 2, concluded: 3 }, notifications: { sent: 3, pending: 0, failed: 1 },
    weekly: [{ week: "Sem 1", current: 3, previous: 2 }, { week: "Sem 2", current: 4, previous: 3 }],
    byCategory: [{ label: "Indisciplina", value: 7 }],
    byClassroom: [{ classroomId: "c1", classroom: "6º A Manhã", shift: "Manhã", enrolled: 32, occurrences: 7, ratePer100: 21.9 }],
    followUps: [{ id: "o1", student: "ALUNO FICTÍCIO", classroom: "6º A Manhã", category: "Indisciplina", occurredAt: "2026-11-10T13:00:00Z", status: "EmAcompanhamento", owner: "Pedagogia" }],
    method: "Contam as ocorrências registradas…",
  },
};

describe("App da escola", () => {
  it("Direção vê o dashboard pedagógico com os indicadores da API", async () => {
    signedInAs(["DIRETOR"], analytics);
    renderAt("/");

    expect(await screen.findByText("Alunos matriculados")).toBeInTheDocument();
    expect(screen.getByText("Comparação entre turmas")).toBeInTheDocument();
    expect(screen.getByText("ALUNO FICTÍCIO")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /exportar/i })).toBeInTheDocument();
    expect(screen.queryByText(/Dados fictícios/)).not.toBeInTheDocument();
    expect(screen.getByText("Usuários e acessos")).toBeInTheDocument();
  });

  it("Professor não vê indicadores, ocorrências nem administração", async () => {
    signedInAs(["PROFESSOR"]);
    renderAt("/");

    expect(await screen.findByText("Olá, Pessoa")).toBeInTheDocument();
    expect(screen.queryByText("Alunos matriculados")).not.toBeInTheDocument();
    expect(screen.queryByText("Ocorrências")).not.toBeInTheDocument();
    expect(screen.queryByText("Usuários e acessos")).not.toBeInTheDocument();
  });

  it("o painel nunca exibe dados dos módulos protegidos", async () => {
    signedInAs(["DIRETOR"], analytics);
    renderAt("/");
    await screen.findByText("Alunos matriculados");

    expect(screen.getByRole("main").textContent ?? "").not.toMatch(/saúde|sigilo|medicamento|alergia/i);
  });

  it("Professor não vê menu, abas nem nenhuma menção aos módulos protegidos", async () => {
    signedInAs(["PROFESSOR"]);
    renderAt("/");
    await screen.findByText(/Olá, Pessoa/);

    expect(document.body.textContent ?? "").not.toMatch(/saúde|sigilo|pendências sigilosas/i);
  });

  it("Administração técnica só pode convidar Professor e Consulta", async () => {
    signedInAs(["ADMIN_TECNICO"], { "/api/access/members": [] });
    renderAt("/usuarios");

    (await screen.findByRole("button", { name: /convidar pessoa/i })).click();
    const options = (await screen.findByLabelText("Papel")).querySelectorAll("option");
    expect([...options].map((o) => o.textContent)).toEqual(["Professor(a)", "Consulta"]);
  });

  it("endereço inexistente mostra página não encontrada", async () => {
    signedInAs(["DIRETOR"]);
    renderAt("/rota-que-nao-existe");

    expect(await screen.findByText("Página não encontrada")).toBeInTheDocument();
  });
});
