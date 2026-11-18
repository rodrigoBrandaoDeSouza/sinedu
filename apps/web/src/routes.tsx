import type { RouteObject } from "react-router";
import { SchoolLayout } from "./layout/school-layout";
import { ComingSoonPage } from "./pages/coming-soon-page";
import { DashboardPage } from "./pages/dashboard-page";
import { AdminPage } from "./pages/admin-page";
import { ClassroomPage } from "./pages/classroom-page";
import { ClassroomsPage } from "./pages/classrooms-page";
import { ImportPage } from "./pages/import-page";
import { ImportsPage } from "./pages/imports-page";
import { StaffPage } from "./pages/staff-page";
import { StudentPage } from "./pages/student-page";
import { StudentsPage } from "./pages/students-page";
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
      { path: "turmas", element: <ClassroomsPage /> },
      { path: "turmas/:id", element: <ClassroomPage /> },
      { path: "alunos", element: <StudentsPage /> },
      { path: "alunos/:id", element: <StudentPage /> },
      { path: "equipe", element: <StaffPage /> },
      soon("ocorrencias", "Ocorrências", "Registro, acompanhamento, ata e linha do tempo.", "F5-01"),
      soon("comunicacoes", "Comunicações", "E-mails aos responsáveis com prévia, autorização e reenvio.", "F5-07"),
      { path: "importacoes", element: <ImportsPage /> },
      { path: "importacoes/:id", element: <ImportPage /> },
      soon("relatorios", "Relatórios", "Indicadores por bimestre, turma e categoria, com exportação.", "F6-04"),
      { path: "usuarios", element: <UsersPage /> },
      { path: "administracao", element: <AdminPage /> },
      soon("*", "Página não encontrada", "O endereço acessado não existe.", "—"),
    ],
  },
];
