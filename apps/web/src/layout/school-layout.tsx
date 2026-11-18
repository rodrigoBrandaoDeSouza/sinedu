import { AppShell, Button, postNavigate, ThemeToggle, type NavSection } from "@sinedu/ui";
import {
  Bell,
  BookOpenCheck,
  ChevronsUpDown,
  ClipboardList,
  FileBarChart,
  GraduationCap,
  LayoutDashboard,
  LogOut,
  Mail,
  Settings,
  Upload,
  Users,
  UsersRound,
} from "lucide-react";
import { Outlet } from "react-router";
import { roleLabels, useSession, type SchoolRole } from "../lib/session";
import { SessionProvider } from "../lib/session-provider";

export function SchoolLayout() {
  return (
    <SessionProvider fallback={<FullPageLoading />}>
      <SchoolShell />
    </SessionProvider>
  );
}

function SchoolShell() {
  const me = useSession();
  const has = (...roles: SchoolRole[]) => me.roles.some((r) => roles.includes(r));
  const pedagogical = has("DIRETOR", "PEDAGOGO");
  const config = has("DIRETOR", "ADMIN_TECNICO");

  const sections: NavSection[] = [
    { items: [{ to: "/", label: "Visão geral", icon: LayoutDashboard, end: true }] },
    {
      title: "Gestão escolar",
      items: [
        { to: "/turmas", label: "Turmas", icon: BookOpenCheck },
        { to: "/alunos", label: "Alunos", icon: GraduationCap },
        ...(config || pedagogical ? [{ to: "/equipe", label: "Equipe", icon: UsersRound }] : []),
      ],
    },
    ...(pedagogical
      ? [
          {
            title: "Pedagógico",
            items: [
              { to: "/ocorrencias", label: "Ocorrências", icon: ClipboardList },
              { to: "/comunicacoes", label: "Comunicações", icon: Mail },
            ],
          },
        ]
      : []),
    ...(config || pedagogical
      ? [
          {
            title: "Dados",
            items: [
              ...(config ? [{ to: "/importacoes", label: "Importações", icon: Upload }] : []),
              ...(pedagogical ? [{ to: "/relatorios", label: "Relatórios", icon: FileBarChart }] : []),
            ],
          },
        ]
      : []),
    ...(config
      ? [
          {
            title: "Configuração",
            items: [
              { to: "/usuarios", label: "Usuários e acessos", icon: Users },
              { to: "/administracao", label: "Administração", icon: Settings },
            ],
          },
        ]
      : []),
  ];

  const initials = me.user.name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase())
    .join("");

  return (
    <AppShell
      brand={{ name: "Sinedu", subtitle: me.school.name }}
      sections={sections}
      searchPlaceholder={pedagogical || has("PROFESSOR", "CONSULTA") ? "Buscar aluno por nome ou código" : undefined}
      topbarLeading={
        <a
          href={me.accountUrl}
          className="hidden h-9 items-center gap-2 rounded-lg px-2 text-sm transition-colors hover:bg-surface-sunken sm:inline-flex"
          title="Trocar de escola"
        >
          <span className="grid size-6 place-items-center rounded-md bg-primary-soft text-[11px] font-bold text-primary-soft-foreground">
            {me.school.name
              .split(" ")
              .filter((w) => w.length > 2)
              .slice(0, 2)
              .map((w) => w[0])
              .join("")}
          </span>
          <span className="max-w-52 truncate font-medium text-foreground">{me.school.name}</span>
          <ChevronsUpDown className="size-4 text-muted-foreground" aria-hidden />
        </a>
      }
      topbarActions={
        <>
          <ThemeToggle className="hidden sm:inline-flex" />
          <Button variant="ghost" size="icon" aria-label="Notificações">
            <Bell />
          </Button>
        </>
      }
      sidebarFooter={
        <div className="flex items-center gap-3 rounded-lg px-2 py-1.5">
          <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary text-xs font-semibold text-primary-foreground">{initials}</span>
          <div className="min-w-0 flex-1 leading-tight">
            <p className="truncate text-sm font-medium text-foreground">{me.user.name}</p>
            <p className="truncate text-xs text-muted-foreground">{me.roles.map((r) => roleLabels[r]).join(" · ")}</p>
          </div>
          <Button variant="ghost" size="icon-sm" aria-label="Sair" title="Sair" onClick={() => postNavigate("/auth/logout")}>
            <LogOut />
          </Button>
        </div>
      }
    >
      <Outlet />
    </AppShell>
  );
}

function FullPageLoading() {
  return (
    <div className="grid min-h-dvh place-items-center bg-background" aria-busy="true" aria-label="Carregando">
      <span className="size-8 animate-spin rounded-full border-2 border-border-strong border-t-primary" />
    </div>
  );
}
