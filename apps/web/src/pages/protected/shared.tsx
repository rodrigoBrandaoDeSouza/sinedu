import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, Input, Textarea } from "@sinedu/ui";
import { Download, FileLock2, History, Paperclip, ShieldCheck, Trash2, Upload } from "lucide-react";
import { useRef, useState, type FormEvent, type ReactNode } from "react";
import { formatDate } from "../../lib/school-api";
import { redirectToLogin } from "../../lib/session";
import { isStepUpRequired, protectedApi, type AttachmentView, type VersionInfo } from "../../lib/protected-api";
import { errorsOf, formatDateTime, formatSize } from "./utils";


/**
 * Modal de step-up: a API respondeu que a autenticação forte tem mais de 15 minutos.
 * Volta ao login único pedindo senha + código e retorna para esta mesma página.
 */
export function StepUpDialog({ open }: { open: boolean }) {
  return (
    <Dialog
      open={open}
      onClose={() => undefined}
      title="Confirme sua identidade"
      description="Dados de Saúde e Proteção exigem que você tenha confirmado sua identidade nos últimos 15 minutos."
      footer={
        <Button variant="primary" onClick={() => redirectToLogin(true)}>
          <ShieldCheck />
          Confirmar com senha e código
        </Button>
      }
    >
      <p className="text-sm text-foreground-secondary">Você volta para esta página logo depois. Nada do que está na tela é perdido no servidor.</p>
    </Dialog>
  );
}

/** Estado padrão de carregamento/erro dos painéis protegidos (o step-up abre o modal). */
export function ProtectedState({ error, children }: { error: unknown; children: ReactNode }) {
  if (isStepUpRequired(error)) {
    return (
      <>
        <Card className="p-6 text-center">
          <FileLock2 className="mx-auto mb-2 size-8 text-muted-foreground" aria-hidden />
          <p className="font-medium text-foreground">Confirme sua identidade para ver estes dados</p>
        </Card>
        <StepUpDialog open />
      </>
    );
  }

  if (error) {
    return <Alert tone="warning" title={error instanceof ApiError ? error.message : "Não foi possível carregar."} />;
  }

  return <>{children}</>;
}

export function Section({ label, value }: { label: string; value?: ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={`mt-0.5 whitespace-pre-wrap text-sm ${value ? "text-foreground" : "text-muted-foreground"}`}>{value || "Não informado"}</dd>
    </div>
  );
}

/** Histórico de versões: lista metadados e decifra uma versão só quando a pessoa a abre (auditado). */
export function VersionHistory<T>({
  queryKey,
  list,
  load,
  render,
}: {
  queryKey: unknown[];
  list: () => Promise<VersionInfo[]>;
  load: (version: number) => Promise<{ reason: string; data: T; version: number; createdAt: string; createdByName: string }>;
  render: (data: T) => ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<number>();
  const versions = useQuery({ queryKey: [...queryKey, "versions"], queryFn: list, enabled: open });
  const version = useQuery({ queryKey: [...queryKey, "version", selected], queryFn: () => load(selected!), enabled: selected !== undefined });

  return (
    <>
      <Button size="sm" variant="ghost" onClick={() => setOpen(true)}>
        <History />
        Histórico
      </Button>
      <Dialog open={open} onClose={() => { setOpen(false); setSelected(undefined); }} title="Histórico de versões" description="Cada alteração gera uma versão nova. Nada é apagado.">
        <ProtectedState error={versions.error ?? version.error}>
          {selected !== undefined && version.data ? (
            <div className="space-y-3">
              <Button size="sm" variant="ghost" onClick={() => setSelected(undefined)}>
                ← Todas as versões
              </Button>
              <p className="text-sm text-foreground">
                Versão {version.data.version} · {version.data.createdByName} · {formatDateTime(version.data.createdAt)}
              </p>
              <Alert tone="info" title="Motivo da alteração">
                {version.data.reason}
              </Alert>
              {render(version.data.data)}
            </div>
          ) : (
            <ol className="divide-y divide-border">
              {versions.data?.map((v) => (
                <li key={v.version} className="flex items-center justify-between gap-3 py-2.5">
                  <div>
                    <p className="text-sm font-medium text-foreground">Versão {v.version}</p>
                    <p className="text-xs text-muted-foreground">
                      {v.createdByName} · {formatDateTime(v.createdAt)}
                    </p>
                  </div>
                  <Button size="sm" onClick={() => setSelected(v.version)}>
                    Ver
                  </Button>
                </li>
              ))}
            </ol>
          )}
        </ProtectedState>
      </Dialog>
    </>
  );
}

/** Documentos anexados a um registro: cifrados na API; download só por aqui. */
export function Attachments({ studentId, owner, ownerId }: { studentId: string; owner: "Health" | "Protection"; ownerId: string }) {
  const client = useQueryClient();
  const key = ["protected", studentId, "attachments", owner, ownerId];
  const list = useQuery({ queryKey: key, queryFn: () => protectedApi.attachments(studentId, owner, ownerId) });
  const [uploading, setUploading] = useState(false);
  const [removing, setRemoving] = useState<AttachmentView>();
  const [error, setError] = useState<unknown>();

  const download = useMutation({ mutationFn: (a: AttachmentView) => protectedApi.downloadAttachment(studentId, a), onError: setError });

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <p className="flex items-center gap-1.5 text-sm font-medium text-foreground">
          <Paperclip className="size-4 text-muted-foreground" aria-hidden />
          Documentos
        </p>
        <Button size="sm" onClick={() => setUploading(true)}>
          <Upload />
          Anexar
        </Button>
      </div>
      {isStepUpRequired(error) ? <StepUpDialog open /> : error ? <Alert tone="critical" title={errorsOf(error).form ?? "Falha no download."} /> : null}
      <ProtectedState error={list.error}>
        {list.data?.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum documento anexado.</p> : null}
        <ul className="space-y-1.5">
          {list.data?.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center gap-2 rounded-lg border border-border px-3 py-2">
              <FileLock2 className="size-4 text-muted-foreground" aria-hidden />
              <div className="min-w-0 flex-1">
                <p className="truncate text-sm text-foreground">{a.fileName}</p>
                <p className="text-xs text-muted-foreground">
                  {a.description ? `${a.description} · ` : ""}
                  {formatSize(a.sizeBytes)} · {a.createdByName} · {formatDateTime(a.createdAt)}
                </p>
              </div>
              <Button size="icon-sm" variant="ghost" aria-label={`Baixar ${a.fileName}`} onClick={() => download.mutate(a)}>
                <Download />
              </Button>
              <Button size="icon-sm" variant="ghost" aria-label={`Retirar ${a.fileName}`} onClick={() => setRemoving(a)}>
                <Trash2 />
              </Button>
            </li>
          ))}
        </ul>
      </ProtectedState>
      <UploadDialog
        open={uploading}
        onClose={() => setUploading(false)}
        onUpload={(file, description) => protectedApi.upload(studentId, owner, ownerId, file, description)}
        onDone={() => client.invalidateQueries({ queryKey: key })}
      />
      <Dialog
        open={removing !== undefined}
        onClose={() => setRemoving(undefined)}
        title="Retirar documento da lista?"
        description="O arquivo cifrado continua guardado e a retirada fica na auditoria. Use para anexos enviados por engano."
        footer={
          <>
            <Button onClick={() => setRemoving(undefined)}>Cancelar</Button>
            <Button
              variant="danger"
              onClick={async () => {
                await protectedApi.removeAttachment(studentId, removing!.id);
                setRemoving(undefined);
                client.invalidateQueries({ queryKey: key });
              }}
            >
              Retirar
            </Button>
          </>
        }
      >
        <p className="text-sm text-foreground">{removing?.fileName}</p>
      </Dialog>
    </div>
  );
}

function UploadDialog({
  open,
  onClose,
  onUpload,
  onDone,
}: {
  open: boolean;
  onClose: () => void;
  onUpload: (file: File, description: string) => Promise<unknown>;
  onDone: () => void;
}) {
  const form = useRef<HTMLFormElement>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [stepUp, setStepUp] = useState(false);
  const send = useMutation({
    mutationFn: ({ file, description }: { file: File; description: string }) => onUpload(file, description),
    onSuccess: () => {
      form.current?.reset();
      onDone();
      onClose();
    },
    onError: (err) => (isStepUpRequired(err) ? setStepUp(true) : setErrors(errorsOf(err))),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const file = d.get("file");
    setErrors({});
    if (!(file instanceof File) || file.size === 0) return setErrors({ file: "Escolha um arquivo." });
    send.mutate({ file, description: String(d.get("description") ?? "").trim() });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Anexar documento"
      description="PDF, PNG ou JPG até 10 MB. O arquivo é cifrado antes de ser guardado."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="protected-upload" disabled={send.isPending}>
            {send.isPending ? "Enviando…" : "Anexar"}
          </Button>
        </>
      }
    >
      <StepUpDialog open={stepUp} />
      <form id="protected-upload" ref={form} onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Arquivo" name="file" type="file" accept=".pdf,.png,.jpg,.jpeg" error={errors.file} />
        <Input label="Descrição (opcional)" name="description" maxLength={300} placeholder="Ex.: atestado médico de novembro" error={errors.description} />
      </form>
    </Dialog>
  );
}

/** Exportação excepcional: justificativa obrigatória; o PDF sai com marca d'água e a ação fica auditada. */
export function ExportButton({ studentId }: { studentId: string }) {
  const [open, setOpen] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [stepUp, setStepUp] = useState(false);
  const run = useMutation({
    mutationFn: (body: { health: boolean; protection: boolean; justification: string }) => protectedApi.exportPdf(studentId, body),
    onSuccess: () => setOpen(false),
    onError: (err) => (isStepUpRequired(err) ? setStepUp(true) : setErrors(errorsOf(err))),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    setErrors({});
    run.mutate({ health: d.get("health") === "on", protection: d.get("protection") === "on", justification: String(d.get("justification") ?? "").trim() });
  };

  return (
    <>
      <Button size="sm" onClick={() => setOpen(true)}>
        <Download />
        Exportação excepcional
      </Button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Exportação excepcional"
        description="Use só quando houver pedido formal (Conselho Tutelar, Justiça, atendimento de saúde). O PDF traz seu nome, a data e o número da exportação em todas as páginas."
        footer={
          <>
            <Button onClick={() => setOpen(false)}>Cancelar</Button>
            <Button variant="primary" type="submit" form="protected-export" disabled={run.isPending}>
              {run.isPending ? "Gerando…" : "Gerar PDF"}
            </Button>
          </>
        }
      >
        <StepUpDialog open={stepUp} />
        <form id="protected-export" onSubmit={submit} className="space-y-4" noValidate>
          {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium text-foreground">O que exportar</legend>
            <label className="flex items-center gap-2 text-sm text-foreground">
              <input type="checkbox" name="health" defaultChecked /> Saúde
            </label>
            <label className="flex items-center gap-2 text-sm text-foreground">
              <input type="checkbox" name="protection" /> Proteção/Sigilo
            </label>
            {errors.sections ? <p className="text-xs text-critical-text">{errors.sections}</p> : null}
          </fieldset>
          <Textarea
            label="Justificativa"
            name="justification"
            rows={3}
            maxLength={1000}
            error={errors.justification}
            hint="Ex.: ofício nº 45/2026 do Conselho Tutelar. Fica guardada de forma cifrada."
          />
        </form>
      </Dialog>
    </>
  );
}

export function ProtectedCard({ title, description, actions, children }: { title: string; description?: string; actions?: ReactNode; children: ReactNode }) {
  return (
    <Card>
      <CardHeader title={title} description={description} actions={actions} />
      <CardBody className="space-y-5">{children}</CardBody>
    </Card>
  );
}

export function UpdatedBy({ name, at, version }: { name: string; at: string; version: number }) {
  return (
    <Badge tone="neutral">
      v{version} · {name} · {formatDate(at.slice(0, 10))}
    </Badge>
  );
}
