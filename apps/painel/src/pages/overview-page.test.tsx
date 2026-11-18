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

describe("Painel da plataforma", () => {
  it("mostra escolas, uso e financeiro", async () => {
    renderAt("/");

    expect(await screen.findByText("Escolas ativas")).toBeInTheDocument();
    expect(screen.getByText("Receita recorrente mensal")).toBeInTheDocument();
    expect(screen.getByText("Colégio Estadual Demonstração")).toBeInTheDocument();
  });

  it("filtra escolas por situação", async () => {
    renderAt("/escolas");

    expect(await screen.findByText("Colégio Estadual Amostra")).toBeInTheDocument();
    expect(screen.getAllByText("Suspensa").length).toBeGreaterThan(0);
  });
});
