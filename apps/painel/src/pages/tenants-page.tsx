import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, Dialog, EmptyState, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { Building2, DatabaseZap, ExternalLink, Mail, Pause, Play, Plus, RotateCw } from "lucide-react";
import { SmtpDialog } from "../components/smtp-dialog";
import { useState, type FormEvent } from "react";
import { useSearchParams } from "react-router";
import { useIsSuperAdmin } from "../lib/session";

export type TenantStatus = "Provisioning" | "ProvisioningFailed" | "Active" | "Suspended" | "Closing" | "Closed";

interface TenantSummary {
  id: string;
  slug: string;
  displayName: string;
  url: string;
  network: string | null;
  status: TenantStatus;
  statusReason: string | null;
  schemaVersion: string | null;
  createdAt: string;
}

interface NetworkRow {
  id: string;
  name: string;
}

const statusView: Record<TenantStatus, { label: string; tone: "good" | "info" | "critical" | "warning" | "neutral" }> = {
  Provisioning: { label: "Provisionando", tone: "info" },
  ProvisioningFailed: { label: "Falha no provisionamento", tone: "critical" },
  Active: { label: "Ativa", tone: "good" },
  Suspended: { label: "Suspensa", tone: "critical" },
  Closing: { label: "Encerrando", tone: "warning" },
  Closed: { label: "Encerrada", tone: "neutral" },
};

export function TenantsPage() {
  const superAdmin = useIsSuperAdmin();
  const client = useQueryClient();
  const [params, setParams] = useSearchParams();
  const [smtpFor, setSmtpFor] = useState<{ id: string; displayName: string }>();
  const [creating, setCreatingState] = useState(params.get("nova") === "1");
  const setCreating = (open: boolean) => {
    setCreatingState(open);
    if (!open && params.has("nova")) setParams({}, { replace: true });
  };
  const version = useQuery({ queryKey: ["schema-version"], queryFn: () => api.get<{ schemaVersion: string }>("/api/metrics") });
  const migrate = useMutation({
    mutationFn: () => api.post<{ updated: number; error: string | null; version: string }>("/api/tenants/migrate"),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["tenants"] });
      client.invalidateQueries({ queryKey: ["metrics"] });
    },
  });

  const { data: tenants, isLoading } = useQuery({
    queryKey: ["tenants"],
    queryFn: () => api.get<TenantSummary[]>("/api/tenants"),
    // Enquanto houver escola sendo criada, acompanha os passos.
    refetchInterval: (q) => (q.state.data?.some((t) => t.status === "Provisioning") ? 2000 : false),
  });

  const action = useMutation({
    mutationFn: ({ id, verb, reason }: { id: string; verb: "retry" | "suspend" | "reactivate"; reason?: string }) =>
      api.post(`/api/tenants/${id}/${verb}`, verb === "retry" ? undefined : { reason }),
    onSuccess: () => client.invalidateQueries({ queryKey: ["tenants"] }),
  });

  const ask = (id: string, verb: "suspend" | "reactivate", label: string) => {
    const reason = prompt(`Motivo para ${label} (fica registrado na auditoria):`);
    if (reason?.trim()) action.mutate({ id, verb, reason });
  };

  const current = version.data?.schemaVersion;
  const outdated = tenants?.filter((t) => current && t.schemaVersion !== current && (t.status === "Active" || t.status === "Suspended")) ?? [];

  return (
    <>
      <PageHeader
        title="Escolas"
        description="Cada escola tem subdomínio, banco de dados e chave de criptografia próprios."
        actions={
          superAdmin ? (
            <Button variant="primary" onClick={() => setCreating(true)}>
              <Plus />
              Nova escola
            </Button>
          ) : null
        }
      />

      {action.error ? <Alert tone="critical" className="mb-4" title={action.error instanceof ApiError ? action.error.message : "Ação não concluída."} /> : null}

      {outdated.length > 0 ? (
        <Alert tone="warning" className="mb-4" title={`${outdated.length} escola(s) com o banco desatualizado`}>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>Ficam em manutenção (sem acesso) até a atualização para a versão {current}.</span>
            {superAdmin ? (
              <Button size="sm" onClick={() => migrate.mutate()} disabled={migrate.isPending}>
                <DatabaseZap />
                {migrate.isPending ? "Atualizando…" : "Atualizar escolas"}
              </Button>
            ) : null}
          </div>
        </Alert>
      ) : null}
      {migrate.data ? (
        <Alert tone={migrate.data.error ? "critical" : "good"} className="mb-4" title={migrate.data.error ? `Atualização parou em ${migrate.data.error}` : `${migrate.data.updated} escola(s) atualizada(s) para ${migrate.data.version}.`} />
      ) : null}

      {!isLoading && tenants?.length === 0 ? (
        <EmptyState icon={Building2} title="Nenhuma escola ainda" description="Cadastre a primeira escola para criar o subdomínio, o banco e convidar a direção." />
      ) : (
        <Card>
          <Table>
            <thead>
              <tr>
                <Th>Escola</Th>
                <Th>Situação</Th>
                <Th>Rede</Th>
                <Th>Versão do banco</Th>
                <Th>Criada em</Th>
                <Th>
                  <span className="sr-only">Ações</span>
                </Th>
              </tr>
            </thead>
            <tbody>
              {tenants?.map((t) => (
                <Tr key={t.id}>
                  <Td>
                    <p className="font-medium">{t.displayName}</p>
                    <a href={t.url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-primary">
                      {new URL(t.url).host}
                      <ExternalLink className="size-3" aria-hidden />
                    </a>
                  </Td>
                  <Td>
                    <Badge tone={statusView[t.status].tone} icon>
                      {statusView[t.status].label}
                    </Badge>
                    {t.statusReason && t.status !== "Active" ? (
                      <p className="mt-1 max-w-64 truncate text-xs text-muted-foreground" title={t.statusReason}>
                        {t.statusReason}
                      </p>
                    ) : null}
                  </Td>
                  <Td className="text-foreground-secondary">{t.network ?? "—"}</Td>
                  <Td>
                    <span className="font-mono text-xs text-foreground-secondary">{t.schemaVersion ?? "—"}</span>
                    {current && t.schemaVersion && t.schemaVersion !== current ? (
                      <Badge tone="warning" icon className="ml-2">
                        Desatualizado
                      </Badge>
                    ) : null}
                  </Td>
                  <Td className="text-foreground-secondary tabular-nums">{new Date(t.createdAt).toLocaleDateString("pt-BR")}</Td>
                  <Td align="right">
                    {superAdmin && (t.status === "Active" || t.status === "Suspended") ? (
                      <Button size="sm" variant="ghost" onClick={() => setSmtpFor({ id: t.id, displayName: t.displayName })}>
                        <Mail />
                        E-mail
                      </Button>
                    ) : null}
                    {superAdmin && t.status === "ProvisioningFailed" ? (
                      <Button size="sm" onClick={() => action.mutate({ id: t.id, verb: "retry" })}>
                        <RotateCw />
                        Tentar de novo
                      </Button>
                    ) : null}
                    {superAdmin && t.status === "Active" ? (
                      <Button size="sm" variant="ghost" onClick={() => ask(t.id, "suspend", "suspender")}>
                        <Pause />
                        Suspender
                      </Button>
                    ) : null}
                    {superAdmin && t.status === "Suspended" ? (
                      <Button size="sm" onClick={() => ask(t.id, "reactivate", "reativar")}>
                        <Play />
                        Reativar
                      </Button>
                    ) : null}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}

      <CreateTenantDialog open={creating} onClose={() => setCreating(false)} />
      {smtpFor ? <SmtpDialog tenant={smtpFor} onClose={() => setSmtpFor(undefined)} /> : null}
    </>
  );
}

function slugify(name: string) {
  return name
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/\b(de|da|do|das|dos|e)\b/g, "")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 40);
}

function CreateTenantDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [slug, setSlug] = useState("");
  const [slugEdited, setSlugEdited] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const { data: networks } = useQuery({ queryKey: ["networks"], queryFn: () => api.get<NetworkRow[]>("/api/networks"), enabled: open });

  const create = useMutation({
    mutationFn: (body: Record<string, unknown>) => api.post("/api/tenants", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["tenants"] });
      setSlug("");
      setSlugEdited(false);
      onClose();
    },
    onError: (err) => {
      if (err instanceof ApiError && Object.keys(err.fieldErrors).length) {
        setErrors(Object.fromEntries(Object.entries(err.fieldErrors).map(([k, v]) => [k, v[0] ?? ""])));
      } else {
        setErrors({ form: err instanceof ApiError ? err.message : "Não foi possível criar a escola." });
      }
    },
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    setErrors({});
    create.mutate({
      displayName: data.get("displayName"),
      slug,
      institutionalCode: data.get("institutionalCode") || null,
      networkId: data.get("networkId") || null,
      firstDirectorName: data.get("firstDirectorName"),
      firstDirectorEmail: data.get("firstDirectorEmail"),
    });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Nova escola"
      description="Cria o subdomínio, o banco de dados, a chave de criptografia e convida a direção. Leva alguns segundos."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="tenant-form" disabled={create.isPending}>
            {create.isPending ? "Criando…" : "Criar escola"}
          </Button>
        </>
      }
    >
      <form id="tenant-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input
          label="Nome da escola"
          name="displayName"
          required
          error={errors.displayName}
          onChange={(e) => !slugEdited && setSlug(slugify(e.target.value))}
        />
        <Input
          label="Subdomínio"
          name="slug"
          value={slug}
          onChange={(e) => {
            setSlugEdited(true);
            setSlug(e.target.value.toLowerCase());
          }}
          error={errors.slug}
          hint={slug ? `Endereço: ${slug}.sinedu.com.br` : "Letras minúsculas, números e hífen."}
          required
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Código INEP (opcional)" name="institutionalCode" inputMode="numeric" />
          <Select
            label="Rede"
            name="networkId"
            options={[{ value: "", label: "Sem rede" }, ...(networks ?? []).map((n) => ({ value: n.id, label: n.name }))]}
          />
        </div>
        <fieldset className="space-y-4 rounded-xl border border-border p-4">
          <legend className="px-1 text-sm font-medium">Primeiro diretor(a)</legend>
          <p className="-mt-2 text-xs text-muted-foreground">
            Só o primeiro diretor é convidado pelo painel. Depois disso, quem concede acessos é a própria escola.
          </p>
          <Input label="Nome completo" name="firstDirectorName" required error={errors.firstDirectorName} />
          <Input label="E-mail" name="firstDirectorEmail" type="email" required error={errors.firstDirectorEmail} />
        </fieldset>
      </form>
    </Dialog>
  );
}
