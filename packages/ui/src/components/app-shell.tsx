import { Menu, Search, X, type LucideIcon } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { NavLink, useLocation } from "react-router";
import { cn } from "../lib/cn";
import { Button } from "./button";

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  /** Número ou texto curto ao lado do item (ex.: pendências). */
  badge?: string;
  end?: boolean;
}

export interface NavSection {
  title?: string;
  items: NavItem[];
}

export interface AppShellProps {
  brand: { name: string; subtitle?: string; mark?: ReactNode };
  sections: NavSection[];
  /** Conteúdo fixo no rodapé da barra lateral (ex.: usuário). */
  sidebarFooter?: ReactNode;
  /** Ações à direita da barra superior. */
  topbarActions?: ReactNode;
  /** Elemento à esquerda da busca (ex.: seletor de escola). */
  topbarLeading?: ReactNode;
  searchPlaceholder?: string;
  children: ReactNode;
}

export function AppShell({
  brand,
  sections,
  sidebarFooter,
  topbarActions,
  topbarLeading,
  searchPlaceholder,
  children,
}: AppShellProps) {
  const [mobileOpen, setMobileOpen] = useState(false);
  const location = useLocation();

  useEffect(() => setMobileOpen(false), [location.pathname]);

  const sidebar = (
    <div className="flex h-full flex-col">
      <div className="flex h-16 items-center gap-3 px-5">
        {brand.mark ?? <DefaultMark />}
        <div className="min-w-0 leading-tight">
          <p className="truncate text-[15px] font-semibold tracking-tight text-foreground">{brand.name}</p>
          {brand.subtitle ? <p className="truncate text-xs text-muted-foreground">{brand.subtitle}</p> : null}
        </div>
      </div>

      <nav className="flex-1 space-y-6 overflow-y-auto px-3 py-4" aria-label="Navegação principal">
        {sections.map((section, i) => (
          <div key={section.title ?? i}>
            {section.title ? (
              <p className="px-3 pb-2 text-[11px] font-semibold tracking-wider text-muted-foreground uppercase">
                {section.title}
              </p>
            ) : null}
            <ul className="space-y-0.5">
              {section.items.map((item) => (
                <li key={item.to}>
                  <NavLink
                    to={item.to}
                    end={item.end}
                    className={({ isActive }) =>
                      cn(
                        "group flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium text-foreground-secondary transition-colors hover:bg-surface-sunken hover:text-foreground",
                        isActive && "bg-sidebar-active text-sidebar-active-foreground hover:bg-sidebar-active hover:text-sidebar-active-foreground",
                      )
                    }
                  >
                    <item.icon className="size-[18px] shrink-0" aria-hidden />
                    <span className="flex-1 truncate">{item.label}</span>
                    {item.badge ? (
                      <span className="rounded-full bg-surface-sunken px-1.5 text-[11px] font-semibold text-foreground-secondary tabular-nums">
                        {item.badge}
                      </span>
                    ) : null}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>

      {sidebarFooter ? <div className="border-t border-border p-3">{sidebarFooter}</div> : null}
    </div>
  );

  return (
    <div className="min-h-dvh bg-background">
      {/* Barra lateral fixa (desktop) */}
      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 border-r border-border bg-sidebar lg:block">{sidebar}</aside>

      {/* Gaveta (mobile/tablet) */}
      {mobileOpen ? (
        <div className="fixed inset-0 z-40 lg:hidden" role="dialog" aria-modal="true" aria-label="Menu">
          <button
            type="button"
            className="absolute inset-0 bg-black/40 backdrop-blur-[2px]"
            aria-label="Fechar menu"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="absolute inset-y-0 left-0 w-72 max-w-[85vw] border-r border-border bg-sidebar shadow-pop">
            <Button
              variant="ghost"
              size="icon-sm"
              className="absolute right-3 top-4"
              onClick={() => setMobileOpen(false)}
              aria-label="Fechar menu"
            >
              <X />
            </Button>
            {sidebar}
          </aside>
        </div>
      ) : null}

      <div className="lg:pl-64">
        <header className="sticky top-0 z-20 flex h-16 items-center gap-3 border-b border-border bg-background/80 px-4 backdrop-blur-md sm:px-6">
          <Button
            variant="ghost"
            size="icon"
            className="lg:hidden"
            onClick={() => setMobileOpen(true)}
            aria-label="Abrir menu"
          >
            <Menu />
          </Button>
          {topbarLeading}
          {searchPlaceholder ? (
            <div className="relative hidden max-w-md flex-1 md:block">
              <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
              <input
                type="search"
                placeholder={searchPlaceholder}
                aria-label={searchPlaceholder}
                autoComplete="off"
                className="h-9 w-full rounded-lg border border-border bg-surface-raised pl-9 pr-14 text-sm placeholder:text-muted-foreground"
              />
              <kbd className="pointer-events-none absolute right-2.5 top-1/2 -translate-y-1/2 rounded border border-border bg-surface-sunken px-1.5 text-[10px] font-medium text-muted-foreground">
                Ctrl K
              </kbd>
            </div>
          ) : (
            <div className="flex-1" />
          )}
          <div className="ml-auto flex items-center gap-2">{topbarActions}</div>
        </header>

        <main className="mx-auto w-full max-w-[1440px] px-4 py-6 sm:px-6 lg:py-8">{children}</main>
      </div>
    </div>
  );
}

function DefaultMark() {
  return (
    <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-primary text-primary-foreground shadow-card">
      <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
        <path d="M3 9.5 12 5l9 4.5-9 4.5L3 9.5Z" strokeLinejoin="round" />
        <path d="M7 11.5V16c0 1.1 2.2 2.5 5 2.5s5-1.4 5-2.5v-4.5" strokeLinecap="round" />
      </svg>
    </span>
  );
}

export interface PageHeaderProps {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
  eyebrow?: ReactNode;
}

export function PageHeader({ title, description, actions, eyebrow }: PageHeaderProps) {
  return (
    <div className="mb-6 flex flex-wrap items-end justify-between gap-4">
      <div className="min-w-0">
        {eyebrow ? <div className="mb-2">{eyebrow}</div> : null}
        <h1 className="text-2xl font-semibold tracking-tight text-foreground sm:text-[28px]">{title}</h1>
        {description ? <p className="mt-1 text-sm text-muted-foreground">{description}</p> : null}
      </div>
      {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
    </div>
  );
}
