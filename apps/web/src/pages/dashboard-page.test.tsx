import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it } from "vitest";
import { routes } from "../routes";

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

describe("Dashboard da escola", () => {
  it("mostra os indicadores do bimestre", async () => {
    renderAt("/");

    expect(await screen.findByText("Alunos matriculados")).toBeInTheDocument();
    expect(screen.getByText("Ocorrências registradas")).toBeInTheDocument();
    expect(screen.getByText("Comparação entre turmas")).toBeInTheDocument();
    expect(screen.getAllByText("Dados fictícios").length).toBeGreaterThan(0);
  });

  it("não exibe nada dos módulos protegidos", async () => {
    renderAt("/");
    await screen.findByText("Alunos matriculados");

    const text = document.body.textContent ?? "";
    expect(text).not.toMatch(/saúde|sigilo|proteção|medicamento|alergia/i);
  });

  it("rotas ainda não implementadas mostram o item do backlog", async () => {
    renderAt("/ocorrencias");

    expect(await screen.findByText("Em construção")).toBeInTheDocument();
    expect(screen.getByText("Backlog F5-01")).toBeInTheDocument();
  });
});
