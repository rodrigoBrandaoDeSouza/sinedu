import { Card, PageHeader } from "@sinedu/ui";
import { ArrowRight, BookOpenCheck, GraduationCap } from "lucide-react";
import { Link } from "react-router";
import { roleLabels, useSession } from "../lib/session";

/** Início para perfis sem acesso aos indicadores pedagógicos (Professor, Consulta, Admin técnico). */
export function WelcomePage() {
  const me = useSession();
  const shortcuts = [
    { to: "/turmas", icon: BookOpenCheck, title: "Turmas", description: me.roles.includes("ADMIN_TECNICO") ? "Estrutura do ano letivo" : "Suas turmas e seus alunos" },
    ...(me.roles.some((r) => r === "PROFESSOR" || r === "CONSULTA")
      ? [{ to: "/alunos", icon: GraduationCap, title: "Alunos", description: "Buscar e consultar dados cadastrais" }]
      : []),
  ];

  return (
    <>
      <PageHeader
        title={`Olá, ${me.user.name.split(" ")[0]}`}
        description={`${me.school.name} · ${me.roles.map((r) => roleLabels[r]).join(", ")}`}
      />
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {shortcuts.map(({ to, icon: Icon, title, description }) => (
          <Link key={to} to={to} className="group">
            <Card className="flex items-center gap-4 p-5 transition-colors group-hover:border-primary/40">
              <span className="grid size-11 place-items-center rounded-xl bg-primary-soft text-primary-soft-foreground">
                <Icon className="size-5" aria-hidden />
              </span>
              <div className="min-w-0 flex-1">
                <p className="font-medium text-foreground">{title}</p>
                <p className="text-sm text-muted-foreground">{description}</p>
              </div>
              <ArrowRight className="size-4 text-muted-foreground transition-transform group-hover:translate-x-0.5" aria-hidden />
            </Card>
          </Link>
        ))}
      </div>
    </>
  );
}
