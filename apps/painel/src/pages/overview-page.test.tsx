import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { fireEvent, render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { routes } from "../routes";

// Métricas FICTÍCIAS para o teste: só contagens, como a API real.
const metrics = {
  schemaVersion: "2026.11.19.1",
  collectedAt: "2026-11-19T03:00:00Z",
  tenants: [
    { tenantId: "1", slug: "escola-demo", name: "Colégio Estadual Demonstração", status: "Active", schemaVersion: "2026.11.19.1", outdated: false, collectedAt: "2026-11-19T03:00:00Z",
      activeStudents: 1031, activeClassrooms: 34, activeMembers: 12, occurrencesLast30Days: 9, emailsSentThisMonth: 4, emailsFailedOpen: 1, commonStorageBytes: 2048000, error: null },
    { tenantId: "2", slug: "escola-modelo", name: "Escola Municipal Modelo", status: "Active", schemaVersion: "2026.11.18.5", outdated: true, collectedAt: "2026-11-19T03:00:00Z",
      activeStudents: 150, activeClassrooms: 29, activeMembers: 3, occurrencesLast30Days: 0, emailsSentThisMonth: 0, emailsFailedOpen: 0, commonStorageBytes: 0, error: null },
  ],
  trend: [{ day: "2026-11-18", students: 1170 }, { day: "2026-11-19", students: 1181 }],
};

function signedInAs(roles: string[], tenants: unknown[] = [], extra: Record<string, unknown> = {}) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/me") return Response.json({ id: "a1", name: "Super Admin", email: "admin@sinedu.local", roles });
      if (url === "/api/tenants") return Response.json(tenants);
      if (url === "/api/metrics") return Response.json(metrics);
      const key = Object.keys(extra).find((k) => url.startsWith(k));
      if (key) return Response.json(extra[key]);
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
  it("visão geral mostra métricas agregadas e alerta de schema desatualizado", async () => {
    signedInAs(["SUPER_ADMIN"]);
    renderAt("/");

    expect(await screen.findByText("Escolas ativas")).toBeInTheDocument();
    expect(screen.getByText("1.181")).toBeInTheDocument();
    expect(screen.getByText("1 escola(s) com schema desatualizado")).toBeInTheDocument();
    expect(screen.queryByText(/Dados fictícios/)).not.toBeInTheDocument();
  });

  it("escolas desatualizadas aparecem com botão de atualização para super admin", async () => {
    signedInAs(["SUPER_ADMIN"], [
      { id: "2", slug: "escola-modelo", displayName: "Escola Municipal Modelo", url: "http://escola-modelo.localhost:5173/", network: null, status: "Active", statusReason: null, schemaVersion: "2026.11.18.5", createdAt: "2026-11-18T12:00:00Z" },
    ]);
    renderAt("/escolas");

    expect(await screen.findByText("1 escola(s) com o banco desatualizado")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /atualizar escolas/i })).toBeInTheDocument();
  });

  it("contas da plataforma: equipe e atendimento só para super admin", async () => {
    signedInAs(["SUPER_ADMIN"], [], {
      "/api/accounts": [{ id: "a1", email: "admin@sinedu.local", fullName: "Super Admin", status: "Active", mfaEnabled: true, lockoutUntil: null, lastLoginAt: null, roles: ["SUPER_ADMIN"] }],
    });
    renderAt("/contas");

    expect(await screen.findByText("Equipe da plataforma")).toBeInTheDocument();
    expect(screen.getByText("Atendimento a contas")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /retirar acesso/i })).not.toBeInTheDocument(); // não retira o próprio
  });

  it("teste de e-mail lista as escolas e o SMTP padrão", async () => {
    signedInAs(["SUPER_ADMIN"], [
      { id: "1", slug: "escola-demo", displayName: "Colégio Estadual Demonstração", url: "", network: null, status: "Active", statusReason: null, schemaVersion: null, createdAt: "2026-11-18T12:00:00Z" },
    ]);
    renderAt("/teste-email");

    const enviarPor = await screen.findByRole("combobox", { name: "Enviar por" });
    expect(enviarPor).toHaveTextContent("SMTP padrão da plataforma");
    fireEvent.click(enviarPor);
    expect(enviarPor).toHaveAttribute("aria-expanded", "true");
    fireEvent.click(await screen.findByRole("option", { name: "Colégio Estadual Demonstração (escola-demo)" }));
    expect(enviarPor).toHaveTextContent("Colégio Estadual Demonstração (escola-demo)");
    expect(enviarPor).toHaveAttribute("aria-expanded", "false");

    // Teclado: seta abre, seta move, Enter escolhe.
    fireEvent.keyDown(enviarPor, { key: "ArrowDown" });
    fireEvent.keyDown(enviarPor, { key: "ArrowUp" });
    fireEvent.keyDown(enviarPor, { key: "Enter" });
    expect(enviarPor).toHaveTextContent("SMTP padrão da plataforma");
    expect(screen.getByRole("button", { name: /enviar e-mail de teste/i })).toBeDisabled();
  });

  it("suporte não acessa contas da plataforma", async () => {
    signedInAs(["SUPORTE"]);
    renderAt("/contas");

    expect(await screen.findByText("Somente super admins gerenciam contas.")).toBeInTheDocument();
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

  // Cobrança (P-06..P-09). Valores FICTÍCIOS.
  const alerts = [{ kind: "fatura_vencida", tenantId: "1", school: "Colégio Estadual Demonstração", message: "Fatura de 10/2026 vencida em 10/10/2026 (40 dia(s)).", contractId: "c1", invoiceId: "i1" }];
  const invoice = {
    id: "i1", contractId: "c1", school: "Colégio Estadual Demonstração", payer: "APMF do Colégio (fictícia)", referenceMonth: "2026-10", students: 1031,
    tierLabel: "301 a 1200 alunos", amount: 1250.5, dueOn: "2026-10-10", status: "Overdue", invoiceNumber: null, issuedOn: null, paidOn: null, paidAmount: null, notes: null,
  };

  it("super admin vê faturas com valor em centavos, faixa e baixa", async () => {
    signedInAs(["SUPER_ADMIN"], [], { "/api/billing/alerts": alerts, "/api/billing/invoices": [invoice] });
    renderAt("/faturas");

    expect(await screen.findByText("Fatura de 10/2026 vencida em 10/10/2026 (40 dia(s)).")).toBeInTheDocument();
    expect(await screen.findByText("301 a 1200 alunos")).toBeInTheDocument();
    expect(screen.getAllByText(/1\.250,50/).length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /baixa/i })).toBeInTheDocument();
  });

  it("suporte só vê os alertas de cobrança", async () => {
    signedInAs(["SUPORTE"], [], { "/api/billing/alerts": alerts, "/api/billing/invoices": [invoice] });
    renderAt("/faturas");

    expect(await screen.findByText("Fatura de 10/2026 vencida em 10/10/2026 (40 dia(s)).")).toBeInTheDocument();
    expect(screen.queryByText("301 a 1200 alunos")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /gerar faturas/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /planos e contratos/i })).not.toBeInTheDocument();
  });

  it("planos mostram as faixas com o valor mensal", async () => {
    signedInAs(["SUPER_ADMIN"], [], {
      "/api/billing/plans": [{ id: "p1", name: "Padrão", isActive: true, contracts: 1, tiers: [{ upToStudents: 300, monthlyPrice: 500 }, { upToStudents: null, monthlyPrice: 900 }] }],
      "/api/billing/contracts": [],
    });
    renderAt("/contratos");

    expect(await screen.findByText("Padrão")).toBeInTheDocument();
    expect(screen.getByText("1 a 300 alunos")).toBeInTheDocument();
    expect(screen.getByText("Acima de 300 alunos")).toBeInTheDocument();
  });
});
