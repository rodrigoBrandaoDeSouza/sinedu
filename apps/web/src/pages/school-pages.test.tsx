import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ThemeProvider } from "@sinedu/ui";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { SchoolRole } from "../lib/session";
import { routes } from "../routes";

const classroom = {
  id: "c1", name: "6º A Manhã", gradeId: "g1", gradeName: "6º ano", stage: "Fundamental", section: "A", shift: "Manha",
  status: "Active", capacity: null, externalCode: null, schoolYearId: "y1", teachers: [{ assignmentId: "t1", staffMemberId: "s1", name: "Prof. Fictício", subject: "Matemática" }],
};

const baseStudent = {
  id: "st1", fullName: "LÍVIA FREITAS COSTA", socialName: null, birthDate: "2014-03-10", age: 12, institutionalCode: "1750500790",
  institutionalEmail: "livia.costa21@aluno.exemplo.edu.br", status: "Active", classroom: { id: "c1", name: "6º A Manhã" },
  enrollments: [{ id: "e1", classroomId: "c1", classroom: "6º A Manhã", year: 2026, status: "Ativa", startsOn: "2026-02-02", endsOn: null, callNumber: 7 }],
  canEdit: false,
};

function signedInAs(roles: SchoolRole[], responses: Record<string, unknown>) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string) => {
      if (url === "/api/me") {
        return Response.json({
          user: { id: "u1", name: "Pessoa Fictícia", email: "pessoa@escola.local" }, roles,
          school: { slug: "escola-teste", name: "Colégio Estadual de Teste" }, mfa: true, authTime: null, accountUrl: "http://conta.localhost:5175/",
        });
      }
      const key = Object.keys(responses).find((k) => url.startsWith(k));
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

describe("Cadastros", () => {
  it("lista turmas agrupadas por etapa com a contagem de alunos", async () => {
    signedInAs(["DIRETOR"], {
      "/api/school/classrooms": [classroom],
      "/api/students/classroom-counts": [{ classroomId: "c1", students: 31 }],
      "/api/school/years": [{ id: "y1", year: 2026, startsOn: "2026-02-02", endsOn: "2026-12-18", status: "Active", terms: [] }],
    });
    renderAt("/turmas");

    expect(await screen.findByText("Ensino Fundamental")).toBeInTheDocument();
    expect(screen.getByText("6º A")).toBeInTheDocument();
    expect(await screen.findByText("31")).toBeInTheDocument();
  });

  it("painel do aluno para Direção mostra endereço, documento e edição", async () => {
    signedInAs(["DIRETOR"], {
      "/api/students/st1": {
        ...baseStudent, canEdit: true, address: "RUA DAS ACÁCIAS, 120 - JARDIM EXEMPLO",
        guardians: [{ id: "g1", name: "JOÃO CARVALHO REIS", relationship: "Pai", isPrimaryContact: true, contactAuthorized: false, phone: "(00) 91234-5678", email: null, document: "427.492.549-94" }],
      },
    });
    renderAt("/alunos/st1");

    expect(await screen.findByText("LÍVIA FREITAS COSTA")).toBeInTheDocument();
    expect(screen.getByText("RUA DAS ACÁCIAS, 120 - JARDIM EXEMPLO")).toBeInTheDocument();
    expect(screen.getByText("427.492.549-94")).toBeInTheDocument();
    expect(screen.getByText("Autorização não registrada")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /editar cadastro/i })).toBeInTheDocument();
    // F2-06: Direção cadastra, edita e desvincula responsáveis.
    expect(screen.getByRole("button", { name: /adicionar/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Desvincular JOÃO CARVALHO REIS" })).toBeInTheDocument();
  });

  it("Professor vê responsáveis, mas não os altera", async () => {
    signedInAs(["PROFESSOR"], {
      "/api/students/st1": {
        ...baseStudent,
        guardians: [{ id: "g1", name: "JOÃO CARVALHO REIS", relationship: "Pai", isPrimaryContact: true, contactAuthorized: true, phone: "(00) 91234-5678", email: null }],
      },
    });
    renderAt("/alunos/st1");

    expect(await screen.findByText("JOÃO CARVALHO REIS")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /adicionar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /desvincular/i })).not.toBeInTheDocument();
  });

  it("perfil Consulta não vê responsáveis nem endereço (campos ausentes na API)", async () => {
    signedInAs(["CONSULTA"], { "/api/students/st1": baseStudent });
    renderAt("/alunos/st1");

    expect(await screen.findByText("LÍVIA FREITAS COSTA")).toBeInTheDocument();
    expect(screen.queryByText("Responsáveis")).not.toBeInTheDocument();
    expect(screen.queryByText("Endereço")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /editar/i })).not.toBeInTheDocument();
  });

  it("aluno fora do acesso mostra aviso sem detalhes", async () => {
    signedInAs(["PROFESSOR"], {});
    renderAt("/alunos/outro");

    expect(await screen.findByText("Aluno não encontrado ou fora do seu acesso.")).toBeInTheDocument();
  });

  it("Administração técnica não tem o menu de alunos", async () => {
    signedInAs(["ADMIN_TECNICO"], {});
    renderAt("/");

    expect((await screen.findAllByText("Turmas")).length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Alunos" })).not.toBeInTheDocument();
  });
});
