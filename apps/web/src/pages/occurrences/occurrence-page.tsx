import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, Input, Textarea } from "@sinedu/ui";
import { ArrowLeft, CheckCircle2, Download, FileText, Paperclip, Pencil, Trash2, Upload, XCircle } from "lucide-react";
import { useRef, useState, type ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router";
import {
  formatDateTime,
  occurrencesApi,
  occurrenceStatusLabels,
  occurrenceStatusTone,
  revisionLabels,
  type OccurrenceDetails,
  type OccurrenceStatus,
} from "../../lib/occurrences-api";
import { errorsOf, formatSize } from "../protected/utils";
import { CommunicationsPanel } from "./communications-panel";

export function OccurrencePage() {
  const { id = "" } = useParams();
  const occurrence = useQuery({ queryKey: ["occurrence", id], queryFn: () => occurrencesApi.get(id) });
  if (occurrence.error instanceof ApiError && occurrence.error.status === 404) {
    return <Alert tone="warning" title="Ocorrência não encontrada." />;
  }

  const o = occurrence.data;
  if (!o) return <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" aria-busy="true" />;

  return (
    <>
      <Link to="/ocorrencias" className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> Ocorrências
      </Link>

      <Card className="mb-4 p-5">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-xl font-semibold tracking-tight text-foreground">{o.category}</h1>
              <Badge tone={occurrenceStatusTone[o.status]} icon={o.status !== "Rascunho"}>
                {occurrenceStatusLabels[o.status]}
              </Badge>
              {o.severity ? <Badge tone="outline">{o.severity}</Badge> : null}
            </div>
            <p className="mt-1 text-sm text-foreground-secondary">
              <Link to={`/alunos/${o.studentId}`} className="font-medium text-foreground hover:underline">
                {o.studentName}
              </Link>
              {o.studentCode ? ` · CGM ${o.studentCode}` : ""} · turma do fato: {o.classroom}
              {o.schoolYear ? ` (${o.schoolYear})` : ""}
            </p>
            <p className="text-sm text-muted-foreground">
              {formatDateTime(o.occurredAt)}
              {o.location ? ` · ${o.location}` : ""}
            </p>
          </div>
          <Actions o={o} />
        </div>
      </Card>

      <div className="grid gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          <Card>
            <CardHeader title="Registro" />
            <CardBody>
              <dl className="space-y-4">
                <Field label="Descrição do fato" value={o.description} />
                <Field label="Envolvidos" value={o.involved} />
                <Field label="Providências" value={o.actionsTaken} />
                <Field label="Encaminhamentos" value={o.referrals} />
                <Field label="Observações" value={o.notes} />
                {o.cancelReason ? <Field label="Motivo do cancelamento" value={o.cancelReason} /> : null}
              </dl>
            </CardBody>
          </Card>
          {o.status !== "Rascunho" ? <CommunicationsPanel occurrence={o} /> : null}
        </div>

        <div className="space-y-4">
          <Card>
            <CardHeader title="Histórico" description="Cada mudança depois do registro gera uma versão." />
            <CardBody>
              <ol className="relative space-y-4 border-l border-border pl-5">
                {o.revisions.map((r) => (
                  <li key={r.version} className="relative">
                    <span aria-hidden className="absolute -left-[25px] top-1.5 size-2.5 rounded-full bg-primary ring-4 ring-surface" />
                    <p className="text-sm font-medium text-foreground">
                      v{r.version} · {revisionLabels[r.action] ?? r.action}
                    </p>
                    <p className="text-xs text-muted-foreground">
                      {r.changedByName} · {formatDateTime(r.changedAt)}
                    </p>
                    <p className="mt-0.5 text-sm text-foreground-secondary">{r.reason}</p>
                  </li>
                ))}
                <li className="relative">
                  <span aria-hidden className="absolute -left-[25px] top-1.5 size-2.5 rounded-full bg-border-strong ring-4 ring-surface" />
                  <p className="text-sm font-medium text-foreground">Criada</p>
                  <p className="text-xs text-muted-foreground">
                    {o.createdByName} · {formatDateTime(o.createdAt)}
                  </p>
                </li>
              </ol>
            </CardBody>
          </Card>
          <AttachmentsCard o={o} />
        </div>
      </div>
    </>
  );
}

function Field({ label, value }: { label: string; value: string | null }) {
  if (!value) return null;
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-0.5 whitespace-pre-wrap text-sm text-foreground">{value}</dd>
    </div>
  );
}

type Pending = { target: OccurrenceStatus; title: string; description: string; label: string; required: boolean } | null;

function Actions({ o }: { o: OccurrenceDetails }) {
  const navigate = useNavigate();
  const client = useQueryClient();
  const [pending, setPending] = useState<Pending>(null);
  const [error, setError] = useState<string>();
  const refresh = () => {
    client.invalidateQueries({ queryKey: ["occurrence", o.id] });
    client.invalidateQueries({ queryKey: ["occurrences"] });
  };
  const run = useMutation({
    mutationFn: (fn: () => Promise<unknown>) => fn(),
    onSuccess: () => {
      setPending(null);
      setError(undefined);
      refresh();
    },
    onError: (err) => setError(errorsOf(err).reason ?? errorsOf(err).form),
  });

  const ata = useMutation({ mutationFn: () => occurrencesApi.ata(o.id), onError: (err) => setError(errorsOf(err).form) });
  const buttons: ReactNode[] = [];
  if (o.status === "Rascunho") {
    buttons.push(
      <Button key="edit" onClick={() => navigate(`/ocorrencias/${o.id}/editar`)}>
        <Pencil /> Editar
      </Button>,
      <Button key="del" variant="ghost" onClick={() => run.mutate(async () => { await occurrencesApi.deleteDraft(o.id); navigate("/ocorrencias"); })}>
        <Trash2 /> Excluir rascunho
      </Button>,
      <Button key="reg" variant="primary" onClick={() => run.mutate(() => occurrencesApi.register(o.id, o.version))}>
        <CheckCircle2 /> Registrar
      </Button>,
    );
  } else {
    buttons.push(
      <Button key="ata" onClick={() => ata.mutate()} disabled={ata.isPending}>
        <FileText /> {ata.isPending ? "Gerando…" : "Ata (PDF)"}
      </Button>,
    );
    if (o.status === "Registrada" || o.status === "EmAcompanhamento") {
      buttons.push(
        <Button key="edit" onClick={() => navigate(`/ocorrencias/${o.id}/editar`)}>
          <Pencil /> Editar
        </Button>,
      );
    }
    if (o.status === "Registrada") {
      buttons.push(
        <Button key="follow" onClick={() => run.mutate(() => occurrencesApi.changeStatus(o.id, "EmAcompanhamento", null, o.version))}>
          Acompanhar
        </Button>,
      );
    }
    if (o.status === "Registrada" || o.status === "EmAcompanhamento") {
      buttons.push(
        <Button
          key="done"
          variant="primary"
          onClick={() => setPending({ target: "Concluida", title: "Concluir ocorrência", description: "Registre o que foi feito para encerrar o acompanhamento.", label: "Conclusão", required: true })}
        >
          <CheckCircle2 /> Concluir
        </Button>,
      );
    }
    if (o.status === "Concluida") {
      buttons.push(
        <Button key="reopen" onClick={() => run.mutate(() => occurrencesApi.changeStatus(o.id, "EmAcompanhamento", "Reaberta para acompanhamento.", o.version))}>
          Reabrir
        </Button>,
      );
    }
    if (o.status !== "Cancelada") {
      buttons.push(
        <Button
          key="cancel"
          variant="ghost"
          onClick={() => setPending({
            target: "Cancelada",
            title: "Cancelar ocorrência",
            description: "Ocorrência registrada não é excluída: ela fica no histórico como cancelada e sai dos indicadores.",
            label: "Motivo do cancelamento",
            required: true,
          })}
        >
          <XCircle /> Cancelar
        </Button>,
      );
    }
  }

  return (
    <div className="flex flex-col items-end gap-2">
      <div className="flex flex-wrap justify-end gap-2">{buttons}</div>
      {error && !pending ? <p className="text-xs text-critical-text">{error}</p> : null}
      <Dialog
        open={pending !== null}
        onClose={() => setPending(null)}
        title={pending?.title ?? ""}
        description={pending?.description}
        footer={
          <>
            <Button onClick={() => setPending(null)}>Voltar</Button>
            <Button variant={pending?.target === "Cancelada" ? "danger" : "primary"} type="submit" form="status-form" disabled={run.isPending}>
              Confirmar
            </Button>
          </>
        }
      >
        <form
          id="status-form"
          noValidate
          onSubmit={(e) => {
            e.preventDefault();
            const reason = String(new FormData(e.currentTarget).get("reason") ?? "").trim();
            run.mutate(() => occurrencesApi.changeStatus(o.id, pending!.target, reason || null, o.version));
          }}
        >
          <Textarea label={pending?.label ?? "Motivo"} name="reason" rows={3} error={error} />
        </form>
      </Dialog>
    </div>
  );
}

function AttachmentsCard({ o }: { o: OccurrenceDetails }) {
  const client = useQueryClient();
  const input = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string>();
  const refresh = () => client.invalidateQueries({ queryKey: ["occurrence", o.id] });
  const upload = useMutation({ mutationFn: (file: File) => occurrencesApi.upload(o.id, file), onSuccess: () => { setError(undefined); refresh(); }, onError: (err) => setError(errorsOf(err).file ?? errorsOf(err).form) });
  const remove = useMutation({ mutationFn: (attachmentId: string) => occurrencesApi.removeAttachment(o.id, attachmentId), onSuccess: refresh });

  return (
    <Card>
      <CardHeader
        title="Anexos"
        description="PDF, PNG ou JPG até 10 MB."
        actions={
          o.status !== "Cancelada" ? (
            <Button size="sm" onClick={() => input.current?.click()} disabled={upload.isPending}>
              <Upload /> Anexar
            </Button>
          ) : null
        }
      />
      <CardBody className="space-y-2">
        <Input label="Arquivo" type="file" ref={input} className="hidden" accept=".pdf,.png,.jpg,.jpeg" onChange={(e) => e.target.files?.[0] && upload.mutate(e.target.files[0])} />
        {error ? <Alert tone="critical" title={error} /> : null}
        {o.attachments.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum anexo.</p> : null}
        {o.attachments.map((a) => (
          <div key={a.id} className="flex items-center gap-2 rounded-lg border border-border px-3 py-2">
            <Paperclip className="size-4 text-muted-foreground" aria-hidden />
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm text-foreground">{a.fileName}</p>
              <p className="text-xs text-muted-foreground">
                {formatSize(a.sizeBytes)} · {a.uploadedByName}
              </p>
            </div>
            <Button size="icon-sm" variant="ghost" aria-label={`Baixar ${a.fileName}`} onClick={() => occurrencesApi.downloadAttachment(o.id, a.id)}>
              <Download />
            </Button>
            <Button size="icon-sm" variant="ghost" aria-label={`Retirar ${a.fileName}`} onClick={() => remove.mutate(a.id)}>
              <Trash2 />
            </Button>
          </div>
        ))}
      </CardBody>
    </Card>
  );
}

