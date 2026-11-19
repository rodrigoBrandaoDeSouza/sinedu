import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SchoolRole } from "../lib/session";
import { routes } from "../routes";

const pending = {
  id: "r1", requesterName: "Suporte Fictício", requesterEmail: "suporte@plataforma.local", reason: "Conferir importação que falhou",
  durationHours: 4, status: "Pending", requestedAt: "2026-11-19T12:00:00Z", decidedAt: null, decidedByName: null, expiresAt: null,
  revokedAt: null, actions: 0, lastActionAt: null,
};

function signedInAs(roles: SchoolRole[], extra: Record<string, unknown> = {}) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/me") {
        return Response.json({
          user: { id: "u1", name: "Pessoa Fictícia", email: "pessoa@escola.local" }, roles,
          school: { slug: "escola-teste", name: "Colégio Estadual de Teste" }, mfa: true, authTime: null, accountUrl: "http://conta.localhost:5175/",
          ...extra,
        });
      }
      if (url === "/api/access/support") return Response.json({ grants: [pending], synced: true });
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

describe("Acesso de suporte (P-11)", () => {
  it("Direção vê o pedido com o motivo e pode liberar ou recusar", async () => {
    signedInAs(["DIRETOR"]);
    renderAt("/suporte");

    expect(await screen.findByText("Suporte Fictício pede acesso por 4 horas")).toBeInTheDocument();
    expect(screen.getByText("Conferir importação que falhou")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /liberar/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: /recusar/i })).toBeEnabled();
  });

  it("Administração técnica acompanha, mas não decide", async () => {
    signedInAs(["ADMIN_TECNICO"]);
    renderAt("/suporte");

    expect(await screen.findByText("Só a Direção pode liberar ou recusar.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /liberar/i })).not.toBeInTheDocument();
  });

  it("sessão de suporte mostra o aviso de somente leitura com o prazo", async () => {
    signedInAs(["SUPORTE_PLATAFORMA"], { supportUntil: "2026-11-19T18:00:00Z" });
    renderAt("/turmas");

    expect(await screen.findByText("Acesso de suporte (somente leitura)")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /acesso de suporte/i })).not.toBeInTheDocument();
  });
});

describe("Escola em encerramento (P-13)", () => {
  it("Direção vê só a tela de encerramento com a exportação", async () => {
    signedInAs(["DIRETOR"], { closing: true });
    renderAt("/alunos");

    expect(await screen.findByText(/está em encerramento/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /baixar exportação/i })).toHaveAttribute("href", "/api/closing/export");
    expect(screen.queryByRole("link", { name: /alunos/i })).not.toBeInTheDocument();
  });

  it("demais papéis não têm a exportação", async () => {
    signedInAs(["PROFESSOR"], { closing: true });
    renderAt("/");

    expect(await screen.findByText(/está em encerramento/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /baixar exportação/i })).not.toBeInTheDocument();
  });
});

describe("Auditoria da escola (P9)", () => {
  it("Direção vê o fato, sem conteúdo, com o aluno", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => {
        if (url === "/api/me") {
          return Response.json({ user: { id: "u1", name: "Diretora Fictícia", email: "d@escola.local" }, roles: ["DIRETOR"], school: { slug: "e", name: "Escola" }, mfa: true, authTime: null, accountUrl: "" });
        }
        if (url.startsWith("/api/audit")) {
          return Response.json({
            total: 1, page: 1, pageSize: 50,
            items: [{ id: 1, occurredAt: "2026-11-19T12:00:00Z", action: "health_viewed", result: "Allowed", user: { id: "u2", name: "Pedagoga Fictícia", email: null }, student: { id: "s1", name: "ALUNO FICTÍCIO" }, isSupportSession: false, ip: null }],
          });
        }
        return new Response(null, { status: 404 });
      }),
    );
    renderAt("/auditoria");

    expect(await screen.findByText("Abriu a ficha de Saúde")).toBeInTheDocument();
    expect(screen.getByText("Pedagoga Fictícia")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "ALUNO FICTÍCIO" })).toHaveAttribute("href", "/alunos/s1");
  });

  it("Pedagogia não tem o menu de auditoria", async () => {
    signedInAs(["PEDAGOGO"]);
    renderAt("/suporte");

    await screen.findAllByText(/pedido/i);
    expect(screen.queryByRole("link", { name: "Auditoria" })).not.toBeInTheDocument();
  });
});
