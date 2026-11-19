import { AppShell, Button, postNavigate, ThemeToggle, type NavSection } from "@sinedu/ui";
import { Bell, Building2, FileText, Gauge, LifeBuoy, LogOut, MailCheck, Network, Receipt, ScrollText, Settings, ShieldCheck } from "lucide-react";
import { Outlet } from "react-router";
import { useSession } from "../lib/session";
import { SessionProvider } from "../lib/session-provider";

const sectionsFor = (superAdmin: boolean): NavSection[] => [
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
      // Planos/contratos só para SUPER_ADMIN; SUPORTE vê os alertas em Faturas.
      ...(superAdmin ? [{ to: "/contratos", label: "Planos e contratos", icon: FileText }] : []),
      { to: "/faturas", label: superAdmin ? "Faturas" : "Alertas de cobrança", icon: Receipt },
    ],
  },
  {
    title: "Sistema",
    items: [
      { to: "/auditoria", label: "Auditoria", icon: ScrollText },
      { to: "/contas", label: "Contas da plataforma", icon: ShieldCheck },
      { to: "/teste-email", label: "Teste de e-mail", icon: MailCheck },
      { to: "/configuracoes", label: "Configurações", icon: Settings },
    ],
  },
];

export function PanelLayout() {
  return (
    <SessionProvider
      fallback={
        <div className="grid min-h-dvh place-items-center bg-background" aria-busy="true" aria-label="Carregando">
          <span className="size-8 animate-spin rounded-full border-2 border-border-strong border-t-primary" />
        </div>
      }
    >
      <PanelShell />
    </SessionProvider>
  );
}

function PanelShell() {
  const me = useSession();
  const sections = sectionsFor(me.roles.includes("SUPER_ADMIN"));
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
          <ThemeToggle className="hidden sm:inline-flex" />
          <Button variant="ghost" size="icon" aria-label="Notificações">
            <Bell />
          </Button>
        </>
      }
      sidebarFooter={
        <div className="flex items-center gap-3 rounded-lg px-2 py-1.5">
          <span className="grid size-8 shrink-0 place-items-center rounded-full bg-foreground text-xs font-semibold text-background">
            {me.name
              .split(" ")
              .slice(0, 2)
              .map((w) => w[0])
              .join("")}
          </span>
          <div className="min-w-0 flex-1 leading-tight">
            <p className="truncate text-sm font-medium text-foreground">{me.name}</p>
            <p className="truncate text-xs text-muted-foreground">{me.roles.includes("SUPER_ADMIN") ? "Super admin" : "Suporte"} · MFA ativo</p>
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
