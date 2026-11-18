import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { fireEvent, render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SchoolRole } from "../../lib/session";
import { routes } from "../../routes";

const occurrence = {
  id: "o1", studentId: "st1", studentName: "LÍVIA FREITAS COSTA", studentCode: "1750500790", classroomId: "c1", classroom: "6º A Manhã", schoolYear: 2026,
  occurredAt: "2026-11-10T13:00:00Z", categoryId: "cat1", category: "Indisciplina", severityId: null, severity: "Leve", location: "Sala 12",
  description: "Discussão fictícia em sala durante a aula.", involved: null, actionsTaken: "Conversa com a aluna", referrals: null, notes: null,
  status: "Registrada", createdByName: "Pedagoga Fictícia", createdAt: "2026-11-10T13:10:00Z", registeredByName: "Pedagoga Fictícia",
  registeredAt: "2026-11-10T13:10:00Z", concludedAt: null, cancelledAt: null, cancelReason: null, version: 1, isAuthor: true,
  revisions: [{ version: 1, action: "registrada", reason: "Registro inicial.", changedByName: "Pedagoga Fictícia", changedAt: "2026-11-10T13:10:00Z" }],
  attachments: [],
};

const preview = {
  occurrenceId: "o1", occurrenceStatus: "Registrada",
  channels: [{ channel: "Email", enabled: true, reason: null }, { channel: "WhatsApp", enabled: false, reason: "Desabilitado" }],
  templates: [{ id: "t1", channel: "Email", name: "Convite para conversar com a escola", subject: "s", body: "b", isActive: true }],
  templateId: "t1",
  recipients: [
    { guardianId: "g1", name: "MÃE FICTÍCIA", relationship: "Mae", email: "mae@exemplo.test", eligible: true, reason: null,
      subject: "Escola: pedido de contato", body: "Olá, MÃE FICTÍCIA. A equipe gostaria de conversar com você sobre Lívia." },
    { guardianId: "g2", name: "PAI FICTÍCIO", relationship: "Pai", email: null, eligible: false, reason: "Sem autorização de contato registrada.", subject: null, body: null },
  ],
};

function signedInAs(roles: SchoolRole[], responses: Record<string, unknown> = {}) {
  const fetchMock = vi.fn(async (url: string) => {
    if (url === "/api/me") {
      return Response.json({
        user: { id: "u1", name: "Pessoa Fictícia", email: "p@escola.local" }, roles,
        school: { slug: "escola-demo", name: "Escola" }, mfa: true, authTime: null, accountUrl: "http://conta.localhost:5175/",
      });
    }
    const key = Object.keys(responses).sort((a, b) => b.length - a.length).find((k) => url.startsWith(k));
    return key ? Response.json(responses[key]) : new Response(null, { status: 404 });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
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

describe("Ocorrências e comunicações", () => {
  it("Professor não vê o menu de ocorrências nem comunicações", async () => {
    signedInAs(["PROFESSOR"]);
    renderAt("/");
    await screen.findByText(/Olá, Pessoa/);
    expect(screen.queryByRole("link", { name: "Ocorrências" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Comunicações" })).not.toBeInTheDocument();
  });

  it("detalhe mostra turma do fato, histórico de versões e ações da situação", async () => {
    signedInAs(["PEDAGOGO"], { "/api/occurrences/o1/communications": [], "/api/occurrences/o1": occurrence });
    renderAt("/ocorrencias/o1");

    expect(await screen.findByText(/turma do fato: 6º A Manhã \(2026\)/)).toBeInTheDocument();
    expect(screen.getByText("v1 · Registrada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /ata \(pdf\)/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /concluir/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /excluir rascunho/i })).not.toBeInTheDocument();
  });

  it("comunicar mostra canais, quem não pode receber e a prévia neutra antes de enviar", async () => {
    signedInAs(["DIRETOR"], {
      "/api/occurrences/o1/communications/preview": preview,
      "/api/occurrences/o1/communications": [],
      "/api/occurrences/o1": occurrence,
    });
    renderAt("/ocorrencias/o1");

    fireEvent.click(await screen.findByRole("button", { name: /comunicar/i }));
    expect(await screen.findByText(/WhatsApp\s+desabilitado/, {}, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByText("Sem autorização de contato registrada.")).toBeInTheDocument();
    expect(screen.getByText(/A equipe gostaria de conversar com você sobre Lívia/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /autorizar e enviar \(1\)/i, hidden: true })).toBeEnabled();
  });

  it("vindo do aluno, o formulário já traz o aluno escolhido com CGM e turma", async () => {
    signedInAs(["DIRETOR"], {
      "/api/occurrences/categories": { categories: [{ id: "cat1", name: "Indisciplina", isActive: true, sortOrder: 1 }], severities: [] },
      "/api/students/st1": {
        id: "st1", fullName: "LÍVIA FREITAS COSTA", socialName: null, birthDate: null, age: null, institutionalCode: "1750500790", institutionalEmail: null,
        status: "Active", classroom: { id: "c1", name: "6º A Manhã" }, enrollments: [], canEdit: true,
      },
    });
    renderAt("/ocorrencias/nova?aluno=st1");

    expect(await screen.findByText("LÍVIA FREITAS COSTA")).toBeInTheDocument();
    expect(screen.getByText("CGM 1750500790 · 6º A Manhã")).toBeInTheDocument();
  });

  it("formulário avisa que violência e proteção vão para o módulo sigiloso", async () => {
    signedInAs(["DIRETOR"], { "/api/occurrences/categories": { categories: [{ id: "cat1", name: "Indisciplina", isActive: true, sortOrder: 1 }], severities: [] } });
    renderAt("/ocorrencias/nova");

    expect(await screen.findByText("Violência, abuso ou proteção não entram aqui")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /registrar ocorrência/i })).toBeInTheDocument();
  });
});
