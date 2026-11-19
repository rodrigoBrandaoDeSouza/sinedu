import type { RouteObject } from "react-router";
import { SchoolLayout } from "./layout/school-layout";
import { DashboardPage } from "./pages/dashboard-page";
import { AdminPage } from "./pages/admin-page";
import { AuditPage } from "./pages/audit-page";
import { ClassroomPage } from "./pages/classroom-page";
import { ClassroomsPage } from "./pages/classrooms-page";
import { ImportPage } from "./pages/import-page";
import { ImportsPage } from "./pages/imports-page";
import { NotFoundPage } from "./pages/not-found-page";
import { ReportsPage } from "./pages/analytics/reports-page";
import { CommunicationsPage } from "./pages/occurrences/communications-page";
import { OccurrenceFormPage } from "./pages/occurrences/occurrence-form-page";
import { OccurrencePage } from "./pages/occurrences/occurrence-page";
import { OccurrencesPage } from "./pages/occurrences/occurrences-page";
import { HealthImportRoute, PendingRoute } from "./pages/protected/lazy-routes";
import { SettingsPage } from "./pages/settings-page";
import { StaffPage } from "./pages/staff-page";
import { StudentPage } from "./pages/student-page";
import { StudentsPage } from "./pages/students-page";
import { SupportPage } from "./pages/support-page";
import { UsersPage } from "./pages/users-page";

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
      { path: "ocorrencias", element: <OccurrencesPage /> },
      { path: "ocorrencias/nova", element: <OccurrenceFormPage /> },
      { path: "ocorrencias/:id", element: <OccurrencePage /> },
      { path: "ocorrencias/:id/editar", element: <OccurrenceFormPage /> },
      { path: "comunicacoes", element: <CommunicationsPage /> },
      { path: "importacoes", element: <ImportsPage /> },
      { path: "importacoes/:id", element: <ImportPage /> },
      { path: "protegido/pendencias", element: <PendingRoute /> },
      { path: "protegido/importar-saude", element: <HealthImportRoute /> },
      { path: "relatorios", element: <ReportsPage /> },
      { path: "usuarios", element: <UsersPage /> },
      { path: "administracao", element: <AdminPage /> },
      { path: "configuracoes", element: <SettingsPage /> },
      { path: "suporte", element: <SupportPage /> },
      { path: "auditoria", element: <AuditPage /> },
      { path: "*", element: <NotFoundPage /> },
    ],
  },
];
