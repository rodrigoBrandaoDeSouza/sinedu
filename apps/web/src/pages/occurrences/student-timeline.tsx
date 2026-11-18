import { useQuery } from "@tanstack/react-query";
import { Badge, Button, Card, CardBody, CardHeader } from "@sinedu/ui";
import { ClipboardList, Mail, Plus } from "lucide-react";
import type { ReactElement } from "react";
import { Link } from "react-router";
import {
  communicationsApi,
  formatDateTime,
  notificationStatusLabels,
  notificationStatusTone,
  occurrencesApi,
  occurrenceStatusLabels,
  occurrenceStatusTone,
} from "../../lib/occurrences-api";

type Entry =
  | { kind: "occurrence"; at: string; id: string; title: string; detail: string; badge: ReactElement }
  | { kind: "communication"; at: string; id: string; occurrenceId: string; title: string; detail: string; badge: ReactElement };

/** Linha do tempo pedagógica (F5-04): ocorrências e comunicações do aluno, mais recentes primeiro. */
export function StudentTimeline({ studentId }: { studentId: string }) {
  const occurrences = useQuery({ queryKey: ["occurrences", { studentId }], queryFn: () => occurrencesApi.list({ studentId }) });
  const communications = useQuery({ queryKey: ["communications", "student", studentId], queryFn: () => communicationsApi.list({ studentId }) });

  const entries: Entry[] = [
    ...(occurrences.data?.items ?? []).map((o) => ({
      kind: "occurrence" as const,
      at: o.occurredAt,
      id: o.id,
      title: o.category + (o.severity ? ` · ${o.severity}` : ""),
      detail: `${o.classroom} · ${o.createdByName}`,
      badge: (
        <Badge tone={occurrenceStatusTone[o.status]} icon={o.status !== "Rascunho"}>
          {occurrenceStatusLabels[o.status]}
        </Badge>
      ),
    })),
    ...(communications.data?.items ?? []).map((n) => ({
      kind: "communication" as const,
      at: n.authorizedAt,
      id: n.id,
      occurrenceId: n.occurrenceId,
      title: `E-mail para ${n.recipientName}`,
      detail: `${n.templateName} · autorizado por ${n.authorizedByName}`,
      badge: (
        <Badge tone={notificationStatusTone[n.status]} icon>
          {notificationStatusLabels[n.status]}
        </Badge>
      ),
    })),
  ].sort((a, b) => b.at.localeCompare(a.at));

  return (
    <Card>
      <CardHeader
        title="Linha do tempo pedagógica"
        description="Ocorrências e comunicações com a família."
        actions={
          <Link to={`/ocorrencias/nova?aluno=${studentId}`}>
            <Button size="sm">
              <Plus /> Nova ocorrência
            </Button>
          </Link>
        }
      />
      <CardBody>
        {entries.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nenhum registro pedagógico.</p>
        ) : (
          <ol className="relative space-y-4 border-l border-border pl-5">
            {entries.map((e) => {
              const Icon = e.kind === "occurrence" ? ClipboardList : Mail;
              return (
                <li key={`${e.kind}-${e.id}`} className="relative">
                  <span aria-hidden className="absolute -left-[29px] top-0.5 grid size-4 place-items-center rounded-full bg-surface ring-4 ring-surface">
                    <Icon className="size-3.5 text-muted-foreground" />
                  </span>
                  <Link to={`/ocorrencias/${e.kind === "occurrence" ? e.id : e.occurrenceId}`} className="group block">
                    <div className="flex flex-wrap items-center gap-2">
                      <p className="text-sm font-medium text-foreground group-hover:underline">{e.title}</p>
                      {e.badge}
                    </div>
                    <p className="text-xs text-muted-foreground">
                      {formatDateTime(e.at)} · {e.detail}
                    </p>
                  </Link>
                </li>
              );
            })}
          </ol>
        )}
      </CardBody>
    </Card>
  );
}
