import type { RouteObject } from "react-router";
import { SchoolLayout } from "./layout/school-layout";
import { ComingSoonPage } from "./pages/coming-soon-page";
import { DashboardPage } from "./pages/dashboard-page";
import { UsersPage } from "./pages/users-page";

const soon = (path: string, title: string, description: string, backlog: string): RouteObject => ({
  path,
  element: <ComingSoonPage title={title} description={description} backlog={backlog} />,
});

export const routes: RouteObject[] = [
  {
    element: <SchoolLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      soon("turmas", "Turmas", "Turmas do ano letivo, professores vinculados e alunos.", "F2-02"),
      soon("alunos", "Alunos", "Busca e painel do aluno com matrículas, responsáveis e histórico.", "F2-05"),
      soon("equipe", "Equipe", "Direção, pedagogia e professores vinculados às turmas.", "F2-03"),
      soon("ocorrencias", "Ocorrências", "Registro, acompanhamento, ata e linha do tempo.", "F5-01"),
      soon("comunicacoes", "Comunicações", "E-mails aos responsáveis com prévia, autorização e reenvio.", "F5-07"),
      soon("importacoes", "Importações", "Planilhas CSV/XLSX com mapeamento assistido e prévia.", "F3-07"),
      soon("relatorios", "Relatórios", "Indicadores por bimestre, turma e categoria, com exportação.", "F6-04"),
      { path: "usuarios", element: <UsersPage /> },
      soon("administracao", "Administração", "Períodos, categorias, modelos de e-mail e cabeçalho da ata.", "F2-01"),
      soon("*", "Página não encontrada", "O endereço acessado não existe.", "—"),
    ],
  },
];
