import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, Select } from "@sinedu/ui";
import { Mail, MessageCircle, RotateCw, Send, X } from "lucide-react";
import { useState } from "react";
import { Link } from "react-router";
import {
  communicationsApi,
  formatDateTime,
  notificationStatusLabels,
  notificationStatusTone,
  type NotificationView,
  type OccurrenceDetails,
} from "../../lib/occurrences-api";
import { relationshipLabels } from "../../lib/school-api";

/** Comunicações de uma ocorrência: histórico, prévia e autorização explícita do envio. */
export function CommunicationsPanel({ occurrence }: { occurrence: OccurrenceDetails }) {
  const [open, setOpen] = useState(false);
  const history = useQuery({ queryKey: ["communications", occurrence.id], queryFn: () => communicationsApi.forOccurrence(occurrence.id) });

  return (
    <Card>
      <CardHeader
        title="Comunicação com responsáveis"
        description="Mensagens neutras: convidam a família a falar com a escola, sem detalhar o fato."
        actions={
          occurrence.status !== "Cancelada" ? (
            <Button size="sm" variant="primary" onClick={() => setOpen(true)}>
              <Send /> Comunicar
            </Button>
          ) : null
        }
      />
      <CardBody className="space-y-3">
        {history.data?.length === 0 ? <p className="text-sm text-muted-foreground">Nenhuma comunicação enviada.</p> : null}
        {history.data?.map((n) => <NotificationRow key={n.id} n={n} />)}
      </CardBody>
      {open ? <ComposeDialog occurrenceId={occurrence.id} onClose={() => setOpen(false)} /> : null}
    </Card>
  );
}

export function NotificationRow({ n, showOccurrenceLink }: { n: NotificationView; showOccurrenceLink?: boolean }) {
  const client = useQueryClient();
  const [expanded, setExpanded] = useState(false);
  const refresh = () => {
    client.invalidateQueries({ queryKey: ["communications"] });
  };
  const retry = useMutation({ mutationFn: () => communicationsApi.retry(n.id), onSettled: refresh });
  const cancel = useMutation({ mutationFn: () => communicationsApi.cancel(n.id), onSettled: refresh });

  return (
    <div className="rounded-xl border border-border p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <Mail className="size-4 text-muted-foreground" aria-hidden />
            <p className="font-medium text-foreground">{n.recipientName}</p>
            <Badge tone={notificationStatusTone[n.status]} icon>
              {notificationStatusLabels[n.status]}
            </Badge>
          </div>
          <p className="mt-0.5 text-xs text-muted-foreground">
            {n.recipientAddress} · {n.templateName} · autorizado por {n.authorizedByName} em {formatDateTime(n.authorizedAt)}
            {n.attempts > 1 ? ` · ${n.attempts} tentativas` : ""}
          </p>
          {n.status === "Falhou" ? (
            <p className="mt-1 text-xs text-critical-text">
              {n.lastError}
              {n.nextAttemptAt ? ` Nova tentativa automática às ${formatDateTime(n.nextAttemptAt)}.` : " Sem novas tentativas automáticas."}
            </p>
          ) : null}
        </div>
        <div className="flex gap-1">
          <Button size="sm" variant="ghost" onClick={() => setExpanded(!expanded)}>
            {expanded ? "Ocultar texto" : "Ver texto"}
          </Button>
          {n.status === "Falhou" ? (
            <Button size="sm" onClick={() => retry.mutate()} disabled={retry.isPending}>
              <RotateCw /> Reenviar
            </Button>
          ) : null}
          {n.status === "Falhou" || n.status === "Pendente" ? (
            <Button size="icon-sm" variant="ghost" aria-label="Cancelar envio" onClick={() => cancel.mutate()}>
              <X />
            </Button>
          ) : null}
        </div>
      </div>
      {showOccurrenceLink ? (
        <Link to={`/ocorrencias/${n.occurrenceId}`} className="mt-1 inline-block text-xs text-primary hover:underline">
          Ver ocorrência
        </Link>
      ) : null}
      {expanded ? (
        <div className="mt-3 rounded-lg bg-surface-sunken p-3 text-sm">
          <p className="font-medium text-foreground">{n.subject}</p>
          <p className="mt-2 whitespace-pre-wrap text-foreground-secondary">{n.body}</p>
          <ol className="mt-3 space-y-0.5 text-xs text-muted-foreground">
            {n.history.map((a) => (
              <li key={a.attemptNo}>
                Tentativa {a.attemptNo} ({a.trigger}) · {formatDateTime(a.startedAt)} · {a.succeeded ? "enviada" : (a.error ?? "falhou")}
              </li>
            ))}
          </ol>
        </div>
      ) : null}
    </div>
  );
}

function ComposeDialog({ occurrenceId, onClose }: { occurrenceId: string; onClose: () => void }) {
  const client = useQueryClient();
  const [templateId, setTemplateId] = useState<string>();
  const [selected, setSelected] = useState<string[]>();
  // Um identificador por abertura do diálogo: clicar duas vezes em "enviar" não manda duas mensagens.
  const [requestId] = useState(() => crypto.randomUUID());
  const [duplicate, setDuplicate] = useState<string>();
  const preview = useQuery({ queryKey: ["communication-preview", occurrenceId, templateId], queryFn: () => communicationsApi.preview(occurrenceId, templateId) });
  const eligible = preview.data?.recipients.filter((r) => r.eligible) ?? [];
  const chosen = selected ?? eligible.map((r) => r.guardianId);

  const send = useMutation({
    mutationFn: (confirmDuplicate: boolean) =>
      communicationsApi.authorize(occurrenceId, { templateId: preview.data!.templateId, guardianIds: chosen, requestId, confirmDuplicate }),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["communications"] });
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && err.status === 409) setDuplicate(err.message);
    },
  });

  const toggle = (id: string) => setSelected(chosen.includes(id) ? chosen.filter((x) => x !== id) : [...chosen, id]);
  const first = preview.data?.recipients.find((r) => r.eligible && chosen.includes(r.guardianId));

  return (
    <Dialog
      open
      onClose={onClose}
      title="Comunicar responsáveis"
      description="Confira destinatários, canal e texto. O envio só acontece depois da sua confirmação."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          {duplicate ? (
            <Button variant="danger" onClick={() => send.mutate(true)} disabled={send.isPending}>
              Enviar mesmo assim
            </Button>
          ) : (
            <Button variant="primary" onClick={() => send.mutate(false)} disabled={send.isPending || chosen.length === 0}>
              <Send /> {send.isPending ? "Enviando…" : `Autorizar e enviar (${chosen.length})`}
            </Button>
          )}
        </>
      }
    >
      {preview.data ? (
        <div className="space-y-4">
          {duplicate ? <Alert tone="warning" title={duplicate} /> : null}
          {send.error && !duplicate ? <Alert tone="critical" title={send.error instanceof ApiError ? send.error.message : "Não foi possível enviar."} /> : null}

          <div className="flex flex-wrap gap-2">
            {preview.data.channels.map((c) => (
              <Badge key={c.channel} tone={c.enabled ? "good" : "neutral"}>
                {c.channel === "Email" ? <Mail className="size-3.5" aria-hidden /> : <MessageCircle className="size-3.5" aria-hidden />}
                {c.channel === "Email" ? "E-mail" : "WhatsApp"} {c.enabled ? "ativo" : "desabilitado"}
              </Badge>
            ))}
          </div>

          <Select
            label="Modelo"
            value={preview.data.templateId}
            onChange={(e) => setTemplateId(e.target.value)}
            options={preview.data.templates.map((t) => ({ value: t.id, label: t.name }))}
          />

          <fieldset className="space-y-2">
            <legend className="mb-1 text-sm font-medium text-foreground">Destinatários</legend>
            {preview.data.recipients.length === 0 ? <p className="text-sm text-muted-foreground">O aluno não tem responsáveis cadastrados.</p> : null}
            {preview.data.recipients.map((r) => (
              <label key={r.guardianId} className={`flex items-start gap-3 rounded-lg border border-border px-3 py-2 ${r.eligible ? "" : "opacity-70"}`}>
                <input type="checkbox" className="mt-1" disabled={!r.eligible} checked={r.eligible && chosen.includes(r.guardianId)} onChange={() => toggle(r.guardianId)} />
                <span className="min-w-0">
                  <span className="block text-sm font-medium text-foreground">
                    {r.name} <span className="font-normal text-muted-foreground">· {relationshipLabels[r.relationship]}</span>
                  </span>
                  <span className="block text-xs text-muted-foreground">{r.eligible ? r.email : r.reason}</span>
                </span>
              </label>
            ))}
            {eligible.length === 0 && preview.data.recipients.length > 0 ? (
              <Alert tone="info" title="Ninguém pode receber ainda">
                Registre o e-mail e a autorização de contato (com o fundamento) no cadastro do aluno, em Responsáveis.
              </Alert>
            ) : null}
          </fieldset>

          {first ? (
            <div className="rounded-lg border border-border bg-surface-sunken p-3 text-sm">
              <p className="text-xs text-muted-foreground">Prévia para {first.name}</p>
              <p className="mt-1 font-medium text-foreground">{first.subject}</p>
              <p className="mt-2 whitespace-pre-wrap text-foreground-secondary">{first.body}</p>
            </div>
          ) : null}
        </div>
      ) : (
        <div className="h-40 animate-pulse rounded-lg bg-surface-sunken" />
      )}
    </Dialog>
  );
}
