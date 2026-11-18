import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SchoolRole } from "../../lib/session";
import { routes } from "../../routes";

const student = {
  id: "st1", fullName: "LÍVIA FREITAS COSTA", socialName: null, birthDate: "2014-03-10", age: 12, institutionalCode: "1750500790",
  institutionalEmail: null, status: "Active", classroom: { id: "c1", name: "6º A Manhã" },
  enrollments: [{ id: "e1", classroomId: "c1", classroom: "6º A Manhã", year: 2026, status: "Ativa", startsOn: "2026-02-02", endsOn: null, callNumber: 7 }],
  canEdit: false,
};

const health = {
  record: {
    id: "h1", version: 2, reviewDueOn: null, updatedAt: "2026-11-18T12:00:00Z", updatedByName: "Diretora Fictícia",
    data: { allergies: "Alergia fictícia a amendoim", bloodType: "O+", emergencyContacts: [{ name: "Avó Fictícia", phone: "(00) 90000-0001" }] },
  },
};

const protection = [
  {
    id: "p1", kind: "MedidaProtetiva", status: "Vencida", validUntil: "2026-10-01", version: 1, updatedAt: "2026-09-01T12:00:00Z", updatedByName: "Pedagoga Fictícia",
    data: { title: "Medida fictícia de afastamento", verification: "Documentado", unauthorizedPeople: [{ name: "Pessoa Impedida Fictícia" }] },
  },
];

type Responder = (url: string) => Response | undefined;

function signedInAs(roles: SchoolRole[], respond: Responder = () => undefined) {
  const fetchMock = vi.fn(async (url: string) => {
    if (url === "/api/me") {
      return Response.json({
        user: { id: "u1", name: "Pessoa Fictícia", email: "p@escola.local" }, roles,
        school: { slug: "escola-demo", name: "Escola" }, mfa: true, authTime: null, accountUrl: "http://conta.localhost:5175/",
      });
    }
    if (url === "/api/students/st1") return Response.json(student);
    if (url.includes("/protected-attachments")) return Response.json([]);
    return respond(url) ?? new Response(null, { status: 404 });
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

const calledProtected = (fetchMock: ReturnType<typeof vi.fn>) =>
  fetchMock.mock.calls.some(([url]) => /\/(health|protection|protected)/.test(String(url)));

afterEach(() => vi.unstubAllGlobals());

describe("Saúde e Proteção/Sigilo", () => {
  it.each<SchoolRole>(["PROFESSOR", "CONSULTA", "ADMIN_TECNICO"])("%s não vê as abas nem dispara chamadas protegidas, mesmo forçando ?aba=saude", async (role) => {
    const fetchMock = signedInAs([role]);
    renderAt("/alunos/st1?aba=saude");

    expect(await screen.findByText("LÍVIA FREITAS COSTA")).toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /saúde/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /proteção/i })).not.toBeInTheDocument();
    expect(calledProtected(fetchMock)).toBe(false);
  });

  it("Direção vê as abas, mas nada é buscado até abrir uma delas", async () => {
    const fetchMock = signedInAs(["DIRETOR"]);
    renderAt("/alunos/st1");

    expect(await screen.findByRole("tab", { name: /saúde/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /proteção/i })).toBeInTheDocument();
    expect(calledProtected(fetchMock)).toBe(false);
  });

  it("aba Saúde mostra a ficha atual com versão e autor", async () => {
    signedInAs(["PEDAGOGO"], (url) => (url === "/api/students/st1/health" ? Response.json(health) : undefined));
    renderAt("/alunos/st1?aba=saude");

    expect(await screen.findByText("Alergia fictícia a amendoim")).toBeInTheDocument();
    expect(screen.getByText(/v2 · Diretora Fictícia/)).toBeInTheDocument();
    expect(screen.getByText(/Avó Fictícia/)).toBeInTheDocument();
  });

  it("step-up vencido abre o modal para confirmar identidade", async () => {
    signedInAs(["DIRETOR"], (url) =>
      url === "/api/students/st1/health"
        ? Response.json({ title: "Confirme sua identidade para continuar.", code: "step_up_required" }, { status: 401 })
        : undefined,
    );
    renderAt("/alunos/st1?aba=saude");

    expect(await screen.findByText("Confirme sua identidade para ver estes dados")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirmar com senha e código/i, hidden: true })).toBeInTheDocument();
  });

  it("aba Proteção mostra situação com ícone e texto e quem não pode retirar", async () => {
    signedInAs(["DIRETOR"], (url) => (url === "/api/students/st1/protection" ? Response.json(protection) : undefined));
    renderAt("/alunos/st1?aba=protecao");

    expect(await screen.findByText("Medida fictícia de afastamento")).toBeInTheDocument();
    expect(screen.getByText("Vencida")).toBeInTheDocument();
    expect(screen.getByText(/Pessoa Impedida Fictícia/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /revogar/i })).toBeInTheDocument();
  });

  it("menu Sigiloso aparece só para Direção e Pedagogia", async () => {
    signedInAs(["PEDAGOGO"]);
    renderAt("/");
    expect(await screen.findByRole("link", { name: "Pendências sigilosas" })).toBeInTheDocument();
  });
});
