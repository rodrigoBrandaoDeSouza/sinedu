import { AppShell, Badge, Button, ThemeToggle, type NavSection } from "@sinedu/ui";
import {
  Bell,
  BookOpenCheck,
  ChevronsUpDown,
  ClipboardList,
  FileBarChart,
  GraduationCap,
  LayoutDashboard,
  Mail,
  Settings,
  Upload,
  Users,
  UsersRound,
} from "lucide-react";
import { Outlet } from "react-router";

const sections: NavSection[] = [
  { items: [{ to: "/", label: "Visão geral", icon: LayoutDashboard, end: true }] },
  {
    title: "Gestão escolar",
    items: [
      { to: "/turmas", label: "Turmas", icon: BookOpenCheck },
      { to: "/alunos", label: "Alunos", icon: GraduationCap },
      { to: "/equipe", label: "Equipe", icon: UsersRound },
    ],
  },
  {
    title: "Pedagógico",
    items: [
      { to: "/ocorrencias", label: "Ocorrências", icon: ClipboardList, badge: "12" },
      { to: "/comunicacoes", label: "Comunicações", icon: Mail },
    ],
  },
  {
    title: "Dados",
    items: [
      { to: "/importacoes", label: "Importações", icon: Upload },
      { to: "/relatorios", label: "Relatórios", icon: FileBarChart },
    ],
  },
  {
    title: "Configuração",
    items: [
      { to: "/usuarios", label: "Usuários e acessos", icon: Users },
      { to: "/administracao", label: "Administração", icon: Settings },
    ],
  },
];

export function SchoolLayout() {
  return (
    <AppShell
      brand={{ name: "Sinedu", subtitle: "Colégio Estadual Demonstração" }}
      sections={sections}
      searchPlaceholder="Buscar aluno por nome ou código"
      topbarLeading={
        <Button variant="ghost" className="hidden gap-2 px-2 sm:inline-flex" aria-label="Trocar de escola">
          <span className="grid size-6 place-items-center rounded-md bg-primary-soft text-[11px] font-bold text-primary-soft-foreground">
            CE
          </span>
          <span className="max-w-44 truncate text-sm font-medium text-foreground">Col. Est. Demonstração</span>
          <ChevronsUpDown className="text-muted-foreground" />
        </Button>
      }
      topbarActions={
        <>
          <Badge tone="warning" icon className="hidden md:inline-flex">
            Dados fictícios
          </Badge>
          <ThemeToggle className="hidden sm:inline-flex" />
          <Button variant="ghost" size="icon" aria-label="Notificações">
            <Bell />
          </Button>
        </>
      }
      sidebarFooter={
        <div className="flex items-center gap-3 rounded-lg px-2 py-1.5">
          <span className="grid size-8 place-items-center rounded-full bg-primary text-xs font-semibold text-primary-foreground">
            DF
          </span>
          <div className="min-w-0 leading-tight">
            <p className="truncate text-sm font-medium text-foreground">Diretora Fictícia</p>
            <p className="truncate text-xs text-muted-foreground">Direção</p>
          </div>
        </div>
      }
    >
      <Outlet />
    </AppShell>
  );
}
