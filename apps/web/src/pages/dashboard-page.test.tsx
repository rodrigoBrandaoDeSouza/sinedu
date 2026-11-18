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
      const body = extra[url];
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

describe("App da escola", () => {
  it("Direção vê o dashboard pedagógico", async () => {
    signedInAs(["DIRETOR"]);
    renderAt("/");

    expect(await screen.findByText("Alunos matriculados")).toBeInTheDocument();
    expect(screen.getByText("Comparação entre turmas")).toBeInTheDocument();
    expect(screen.getByText(/Dados fictícios/)).toBeInTheDocument();
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

  it("nunca exibe nada dos módulos protegidos", async () => {
    signedInAs(["DIRETOR"]);
    renderAt("/");
    await screen.findByText("Alunos matriculados");

    expect(document.body.textContent ?? "").not.toMatch(/saúde|sigilo|medicamento|alergia/i);
  });

  it("Administração técnica só pode convidar Professor e Consulta", async () => {
    signedInAs(["ADMIN_TECNICO"], { "/api/access/members": [] });
    renderAt("/usuarios");

    (await screen.findByRole("button", { name: /convidar pessoa/i })).click();
    const options = (await screen.findByLabelText("Papel")).querySelectorAll("option");
    expect([...options].map((o) => o.textContent)).toEqual(["Professor(a)", "Consulta"]);
  });

  it("rotas ainda não implementadas mostram o item do backlog", async () => {
    signedInAs(["DIRETOR"]);
    renderAt("/ocorrencias");

    expect(await screen.findByText("Em construção")).toBeInTheDocument();
    expect(screen.getByText("Backlog F5-01")).toBeInTheDocument();
  });
});
