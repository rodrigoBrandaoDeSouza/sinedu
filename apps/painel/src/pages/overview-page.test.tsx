import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { routes } from "../routes";

function signedInAs(roles: string[], tenants: unknown[] = []) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/me") return Response.json({ id: "a1", name: "Super Admin", email: "admin@sinedu.local", roles });
      if (url === "/api/tenants") return Response.json(tenants);
      return new Response(null, { status: 404 });
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

describe("Painel da plataforma", () => {
  it("visão geral marca os dados como fictícios", async () => {
    signedInAs(["SUPER_ADMIN"]);
    renderAt("/");

    expect(await screen.findByText("Escolas ativas")).toBeInTheDocument();
    expect(screen.getByText(/Dados fictícios/)).toBeInTheDocument();
  });

  it("lista escolas reais com situação", async () => {
    signedInAs(["SUPER_ADMIN"], [
      { id: "1", slug: "escola-demo", displayName: "Colégio Estadual Demonstração", url: "http://escola-demo.localhost:5173/", network: null, status: "Active", statusReason: null, schemaVersion: "2026.11.18.1", createdAt: "2026-11-18T12:00:00Z" },
      { id: "2", slug: "escola-x", displayName: "Escola X", url: "http://escola-x.localhost:5173/", network: null, status: "ProvisioningFailed", statusReason: "NpgsqlException: timeout", schemaVersion: null, createdAt: "2026-11-18T12:00:00Z" },
    ]);
    renderAt("/escolas");

    expect(await screen.findByText("Colégio Estadual Demonstração")).toBeInTheDocument();
    expect(screen.getByText("Falha no provisionamento")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /tentar de novo/i })).toBeInTheDocument();
  });

  it("suporte não vê ações de super admin", async () => {
    signedInAs(["SUPORTE"], [
      { id: "1", slug: "escola-demo", displayName: "Colégio Estadual Demonstração", url: "http://escola-demo.localhost:5173/", network: null, status: "Active", statusReason: null, schemaVersion: "2026.11.18.1", createdAt: "2026-11-18T12:00:00Z" },
    ]);
    renderAt("/escolas");

    expect(await screen.findByText("Colégio Estadual Demonstração")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /nova escola/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /suspender/i })).not.toBeInTheDocument();
  });
});
