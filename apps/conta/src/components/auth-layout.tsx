import { ThemeToggle } from "@sinedu/ui";
import type { ReactNode } from "react";

export function AuthLayout({ title, description, children, footer }: { title: string; description?: ReactNode; children: ReactNode; footer?: ReactNode }) {
  return (
    <div className="relative flex min-h-dvh flex-col bg-background">
      <div
        aria-hidden
        className="pointer-events-none absolute inset-x-0 top-0 h-80 bg-[radial-gradient(60%_100%_at_50%_0%,var(--primary-soft),transparent)]"
      />
      <header className="relative flex items-center justify-between px-5 py-4 sm:px-8">
        <div className="flex items-center gap-2.5">
          <span className="grid size-9 place-items-center rounded-xl bg-primary text-primary-foreground shadow-card">
            <svg viewBox="0 0 24 24" className="size-5" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden>
              <path d="M3 9.5 12 5l9 4.5-9 4.5L3 9.5Z" strokeLinejoin="round" />
              <path d="M7 11.5V16c0 1.1 2.2 2.5 5 2.5s5-1.4 5-2.5v-4.5" strokeLinecap="round" />
            </svg>
          </span>
          <span className="text-[15px] font-semibold tracking-tight text-foreground">Sinedu</span>
        </div>
        <ThemeToggle />
      </header>

      <main className="relative flex flex-1 items-start justify-center px-4 pb-16 pt-6 sm:items-center sm:pt-0">
        <div className="w-full max-w-[420px]">
          <div className="rounded-(--radius-card) border border-border bg-surface p-6 shadow-card sm:p-8">
            <h1 className="text-xl font-semibold tracking-tight text-foreground">{title}</h1>
            {description ? <p className="mt-1.5 text-sm text-muted-foreground">{description}</p> : null}
            <div className="mt-6">{children}</div>
          </div>
          {footer ? <div className="mt-4 text-center text-sm text-muted-foreground">{footer}</div> : null}
        </div>
      </main>

      <footer className="relative px-5 pb-6 text-center text-xs text-muted-foreground">
        Acesso restrito a equipes escolares autorizadas. Todas as entradas são registradas.
      </footer>
    </div>
  );
}
