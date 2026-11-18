import { AppShell, Badge, Button, ThemeToggle, type NavSection } from "@sinedu/ui";
import { Bell, Building2, FileText, Gauge, LifeBuoy, Network, Receipt, ScrollText, Settings, ShieldCheck } from "lucide-react";
import { Outlet } from "react-router";

const sections: NavSection[] = [
  { items: [{ to: "/", label: "Visão geral", icon: Gauge, end: true }] },
  {
    title: "Plataforma",
    items: [
      { to: "/escolas", label: "Escolas", icon: Building2 },
      { to: "/redes", label: "Redes", icon: Network },
      { to: "/suporte", label: "Acessos de suporte", icon: LifeBuoy },
    ],
  },
  {
    title: "Financeiro",
    items: [
      { to: "/contratos", label: "Contratos", icon: FileText },
      { to: "/faturas", label: "Faturas", icon: Receipt, badge: "1" },
    ],
  },
  {
    title: "Sistema",
    items: [
      { to: "/auditoria", label: "Auditoria", icon: ScrollText },
      { to: "/contas", label: "Contas da plataforma", icon: ShieldCheck },
      { to: "/configuracoes", label: "Configurações", icon: Settings },
    ],
  },
];

export function PanelLayout() {
  return (
    <AppShell
      brand={{
        name: "Sinedu",
        subtitle: "Painel da plataforma",
        mark: (
          <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-foreground text-background shadow-card">
            <Gauge className="size-5" aria-hidden />
          </span>
        ),
      }}
      sections={sections}
      searchPlaceholder="Buscar escola, rede ou contrato"
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
          <span className="grid size-8 place-items-center rounded-full bg-foreground text-xs font-semibold text-background">SA</span>
          <div className="min-w-0 leading-tight">
            <p className="truncate text-sm font-medium text-foreground">Super admin</p>
            <p className="truncate text-xs text-muted-foreground">MFA ativo</p>
          </div>
        </div>
      }
    >
      <Outlet />
    </AppShell>
  );
}
