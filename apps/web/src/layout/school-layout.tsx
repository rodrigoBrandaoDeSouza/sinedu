import { Alert, AppShell, Button, postNavigate, ThemeToggle, type NavSection } from "@sinedu/ui";
import {
  Archive,
  Bell,
  BookOpenCheck,
  CalendarClock,
  ChevronsUpDown,
  ClipboardList,
  Download,
  FileBarChart,
  GraduationCap,
  Headset,
  HeartPulse,
  LayoutDashboard,
  LogOut,
  Mail,
  Settings,
  SlidersHorizontal,
  Upload,
  Users,
  UsersRound,
} from "lucide-react";
import { Outlet, useNavigate } from "react-router";
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
  if (me.closing) return <ClosingScreen />;
  return <SchoolNavigation />;
}

function SchoolNavigation() {
  const me = useSession();
  const navigate = useNavigate();
  const has = (...roles: SchoolRole[]) => me.roles.some((r) => roles.includes(r));
  const pedagogical = has("DIRETOR", "PEDAGOGO");
  const config = has("DIRETOR", "ADMIN_TECNICO");

  const sections: NavSection[] = [
    { items: [{ to: "/", label: "Visão geral", icon: LayoutDashboard, end: true }] },
    {
      title: "Gestão escolar",
      items: [
        { to: "/turmas", label: "Turmas", icon: BookOpenCheck },
        ...(has("ADMIN_TECNICO") && !pedagogical && !has("PROFESSOR", "CONSULTA") ? [] : [{ to: "/alunos", label: "Alunos", icon: GraduationCap }]),
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
    ...(pedagogical
      ? [
          {
            title: "Sigiloso",
            items: [
              { to: "/protegido/pendencias", label: "Pendências sigilosas", icon: CalendarClock },
              { to: "/protegido/importar-saude", label: "Importar saúde", icon: HeartPulse },
            ],
          },
        ]
      : []),
    {
      title: "Configuração",
      items: [
        ...(config
          ? [
              { to: "/usuarios", label: "Usuários e acessos", icon: Users },
              { to: "/suporte", label: "Acesso de suporte", icon: Headset },
              { to: "/administracao", label: "Administração", icon: Settings },
            ]
          : []),
        { to: "/configuracoes", label: "Configurações", icon: SlidersHorizontal },
      ],
    },
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
      searchPlaceholder={pedagogical || has("PROFESSOR", "CONSULTA", "SUPORTE_PLATAFORMA") ? "Buscar aluno por nome ou CGM" : undefined}
      onSearch={(q) => navigate(`/alunos${q ? `?q=${encodeURIComponent(q)}` : ""}`)}
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
      {me.supportUntil ? (
        <Alert tone="warning" title="Acesso de suporte (somente leitura)" className="mb-6">
          Liberado pela Direção da escola até {new Date(me.supportUntil).toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" })}. Tudo o que você
          abre fica registrado para a escola.
        </Alert>
      ) : null}
      <Outlet />
    </AppShell>
  );
}

/** P-13: escola em encerramento. Tudo fechado; a Direção baixa a exportação dos dados comuns (sem Saúde/Proteção). */
function ClosingScreen() {
  const me = useSession();
  const director = me.roles.includes("DIRETOR");
  return (
    <div className="grid min-h-dvh place-items-center bg-background px-4">
      <div className="w-full max-w-lg rounded-(--radius-card) border border-border bg-surface p-6 shadow-card">
        <span className="grid size-10 place-items-center rounded-xl bg-warning-soft text-warning-text">
          <Archive className="size-5" aria-hidden />
        </span>
        <h1 className="mt-4 text-xl font-semibold tracking-tight text-foreground">{me.school.name} está em encerramento</h1>
        <p className="mt-2 text-sm text-foreground-secondary">
          O acesso ao sistema foi encerrado. Os dados ficam guardados por um período e depois são destruídos de forma definitiva.
        </p>
        {director ? (
          <>
            <p className="mt-4 text-sm text-foreground-secondary">
              Baixe a exportação com os dados comuns da escola (turmas, alunos, matrículas, responsáveis, equipe e ocorrências), em planilhas.
              Saúde e Proteção/Sigilo não são exportados. Guarde o arquivo em local seguro: ele tem dados pessoais de estudantes.
            </p>
            <a
              href="/api/closing/export"
              download
              className="mt-4 inline-flex h-10 items-center gap-2 rounded-lg bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary-hover"
            >
              <Download className="size-4" aria-hidden />
              Baixar exportação (ZIP)
            </a>
          </>
        ) : (
          <p className="mt-4 text-sm text-foreground-secondary">Em caso de dúvida, procure a Direção da escola.</p>
        )}
        <div className="mt-6 border-t border-border pt-4">
          <Button variant="ghost" size="sm" onClick={() => postNavigate("/auth/logout")}>
            <LogOut /> Sair
          </Button>
        </div>
      </div>
    </div>
  );
}

function FullPageLoading() {
  return (
    <div className="grid min-h-dvh place-items-center bg-background" aria-busy="true" aria-label="Carregando">
      <span className="size-8 animate-spin rounded-full border-2 border-border-strong border-t-primary" />
    </div>
  );
}
