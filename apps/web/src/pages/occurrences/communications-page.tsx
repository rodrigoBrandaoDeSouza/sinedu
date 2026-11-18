import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Dialog, EmptyState, Input, PageHeader, Select, Textarea } from "@sinedu/ui";
import { Mail, Pencil } from "lucide-react";
import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { communicationsApi, notificationStatusLabels, type NotificationStatus, type Template } from "../../lib/occurrences-api";
import { useHasRole } from "../../lib/session";
import { errorsOf } from "../protected/utils";
import { NotificationRow } from "./communications-panel";

/** Todas as comunicações da escola (fila, falhas, reenvio) e, para a Direção, os modelos de mensagem. */
export function CommunicationsPage() {
  const director = useHasRole("DIRETOR");
  const [params, setParams] = useSearchParams();
  const tab = params.get("aba") === "modelos" && director ? "modelos" : "envios";

  return (
    <>
      <PageHeader title="Comunicações" description="E-mails aos responsáveis: cada envio foi autorizado por alguém da equipe e fica registrado." />
      {director ? (
        <div role="tablist" className="mb-4 flex gap-1 border-b border-border">
          {(
            [
              ["envios", "Envios"],
              ["modelos", "Modelos de mensagem"],
            ] as const
          ).map(([value, label]) => (
            <button
              key={value}
              role="tab"
              type="button"
              aria-selected={tab === value}
              onClick={() => setParams(value === "envios" ? {} : { aba: value }, { replace: true })}
              className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium ${tab === value ? "border-primary text-foreground" : "border-transparent text-muted-foreground hover:text-foreground"}`}
            >
              {label}
            </button>
          ))}
        </div>
      ) : null}
      {tab === "envios" ? <Sent /> : <Templates />}
    </>
  );
}

function Sent() {
  const [status, setStatus] = useState<NotificationStatus | "">("");
  const list = useQuery({ queryKey: ["communications", "all", status], queryFn: () => communicationsApi.list({ status: status || undefined }) });

  return (
    <>
      <div className="mb-4">
        <Select
          label="Situação"
          value={status}
          onChange={(e) => setStatus(e.target.value as NotificationStatus | "")}
          options={[{ value: "", label: "Todas" }, ...Object.entries(notificationStatusLabels).map(([value, label]) => ({ value, label }))]}
        />
      </div>
      {list.data?.total === 0 ? (
        <EmptyState icon={Mail} title="Nenhuma comunicação" description="Comunicações são preparadas a partir de uma ocorrência registrada." />
      ) : (
        <div className="space-y-3">
          {list.data?.items.map((n) => <NotificationRow key={n.id} n={n} showOccurrenceLink />)}
        </div>
      )}
    </>
  );
}

function Templates() {
  const templates = useQuery({ queryKey: ["communication-templates"], queryFn: communicationsApi.templates });
  const [editing, setEditing] = useState<Template>();

  return (
    <>
      <Alert tone="info" className="mb-4" title="Mensagens neutras, mínimo necessário">
        Os modelos só aceitam os marcadores {"{responsavel}"}, {"{aluno}"} (primeiro nome), {"{escola}"}, {"{data}"} e {"{contato_escola}"}. Não há como incluir a descrição
        do fato nem dados de saúde ou proteção.
      </Alert>
      <div className="space-y-3">
        {templates.data?.map((t) => (
          <Card key={t.id} className="p-4">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div>
                <div className="flex items-center gap-2">
                  <p className="font-medium text-foreground">{t.name}</p>
                  <Badge tone={t.isActive ? "good" : "neutral"} icon>
                    {t.isActive ? "Ativo" : "Inativo"}
                  </Badge>
                </div>
                <p className="mt-1 text-sm text-foreground-secondary">{t.subject}</p>
              </div>
              <Button size="sm" onClick={() => setEditing(t)}>
                <Pencil /> Editar
              </Button>
            </div>
            <p className="mt-3 whitespace-pre-wrap rounded-lg bg-surface-sunken p-3 text-sm text-foreground-secondary">{t.body}</p>
          </Card>
        ))}
      </div>
      {editing ? <TemplateDialog template={editing} onClose={() => setEditing(undefined)} /> : null}
    </>
  );
}

function TemplateDialog({ template, onClose }: { template: Template; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const save = useMutation({
    mutationFn: (body: { name: string; subject: string; body: string; isActive: boolean }) => communicationsApi.updateTemplate(template.id, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["communication-templates"] });
      onClose();
    },
    onError: (err) => setErrors(errorsOf(err)),
  });
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    setErrors({});
    save.mutate({ name: String(d.get("name")), subject: String(d.get("subject")), body: String(d.get("body")), isActive: d.get("isActive") === "on" });
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title="Editar modelo"
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="template-form" disabled={save.isPending}>
            Salvar
          </Button>
        </>
      }
    >
      <form id="template-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Nome" name="name" defaultValue={template.name} error={errors.name} />
        <Input label="Assunto" name="subject" defaultValue={template.subject} error={errors.subject} />
        <Textarea label="Texto" name="body" rows={8} defaultValue={template.body} error={errors.body} />
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" name="isActive" defaultChecked={template.isActive} /> Modelo ativo
        </label>
      </form>
    </Dialog>
  );
}
