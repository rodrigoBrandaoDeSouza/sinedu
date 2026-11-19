import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, EmptyState, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { Network as NetworkIcon, Pencil, Plus, Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { fieldErrors } from "../lib/billing-api";
import { useIsSuperAdmin } from "../lib/session";

type NetworkKind = "Estadual" | "Municipal" | "Particular" | "Outra";

interface NetworkRow {
  id: string;
  name: string;
  kind: NetworkKind;
  uf: string | null;
  municipality: string | null;
  schools: number;
}

interface TenantRow {
  id: string;
  slug: string;
  displayName: string;
  status: string;
  networkId: string | null;
}

const kindLabels: Record<NetworkKind, string> = { Estadual: "Estadual", Municipal: "Municipal", Particular: "Particular", Outra: "Outra" };

/** P-04 (D5): redes só agrupam escolas no painel (e ajudam nos contratos). Não existe visão de rede no MVP. */
export function NetworksPage() {
  const superAdmin = useIsSuperAdmin();
  const client = useQueryClient();
  const networks = useQuery({ queryKey: ["networks"], queryFn: () => api.get<NetworkRow[]>("/api/networks") });
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: () => api.get<TenantRow[]>("/api/tenants") });
  const [editing, setEditing] = useState<NetworkRow | "new">();
  const refresh = () => {
    client.invalidateQueries({ queryKey: ["networks"] });
    client.invalidateQueries({ queryKey: ["tenants"] });
  };
  const link = useMutation({
    mutationFn: ({ tenantId, networkId }: { tenantId: string; networkId: string | null }) => api.put(`/api/tenants/${tenantId}/network`, { networkId }),
    onSuccess: refresh,
  });
  const remove = useMutation({ mutationFn: (id: string) => api.delete(`/api/networks/${id}`), onSuccess: refresh });
  const error = link.error ?? remove.error;

  const schools = (tenants.data ?? []).filter((t) => t.status !== "Closed");
  const networkOptions = [{ value: "", label: "Sem rede" }, ...(networks.data ?? []).map((n) => ({ value: n.id, label: n.name }))];

  return (
    <>
      <PageHeader
        title="Redes"
        description="Agrupamento das escolas por rede estadual, municipal ou particular. Cada escola continua com os próprios dados."
        actions={
          superAdmin ? (
            <Button variant="primary" onClick={() => setEditing("new")}>
              <Plus /> Nova rede
            </Button>
          ) : null
        }
      />
      {error ? <Alert tone="critical" title={error instanceof ApiError ? error.message : "Não foi possível concluir."} className="mb-4" /> : null}

      <div className="grid gap-4 xl:grid-cols-2">
        <Card className="self-start">
          <CardHeader title="Redes cadastradas" />
          {networks.data?.length === 0 ? (
            <CardBody>
              <EmptyState icon={NetworkIcon} title="Nenhuma rede" description="Crie uma rede para agrupar escolas, por exemplo a rede estadual do Paraná." />
            </CardBody>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Rede</Th>
                  <Th>Tipo</Th>
                  <Th className="text-right">Escolas</Th>
                  {superAdmin ? (
                    <Th className="text-right">
                      <span className="sr-only">Ações</span>
                    </Th>
                  ) : null}
                </tr>
              </thead>
              <tbody>
                {networks.data?.map((n) => (
                  <Tr key={n.id}>
                    <Td>
                      <p className="font-medium">{n.name}</p>
                      <p className="text-xs text-muted-foreground">{[n.municipality, n.uf].filter(Boolean).join(" · ") || "—"}</p>
                    </Td>
                    <Td>
                      <Badge tone="neutral">{kindLabels[n.kind]}</Badge>
                    </Td>
                    <Td className="text-right tabular-nums">{n.schools}</Td>
                    {superAdmin ? (
                      <Td className="text-right">
                        <div className="flex justify-end gap-1">
                          <Button size="sm" variant="ghost" onClick={() => setEditing(n)}>
                            <Pencil /> Editar
                          </Button>
                          <Button
                            size="sm"
                            variant="ghost"
                            aria-label={`Excluir ${n.name}`}
                            disabled={n.schools > 0 || remove.isPending}
                            title={n.schools > 0 ? "Desvincule as escolas antes de excluir" : "Excluir rede"}
                            onClick={() => {
                              if (confirm(`Excluir a rede ${n.name}?`)) remove.mutate(n.id);
                            }}
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      </Td>
                    ) : null}
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </Card>

        <Card className="self-start">
          <CardHeader title="Escolas e redes" description={superAdmin ? "Escolha a rede de cada escola." : undefined} />
          <Table>
            <thead>
              <tr>
                <Th>Escola</Th>
                <Th className="w-64">Rede</Th>
              </tr>
            </thead>
            <tbody>
              {schools.map((t) => (
                <Tr key={t.id}>
                  <Td>
                    <p className="font-medium">{t.displayName}</p>
                    <p className="text-xs text-muted-foreground">{t.slug}</p>
                  </Td>
                  <Td>
                    {superAdmin ? (
                      <Select
                        label={`Rede de ${t.displayName}`}
                        hideLabel
                        value={t.networkId ?? ""}
                        disabled={link.isPending}
                        onChange={(e) => link.mutate({ tenantId: t.id, networkId: e.target.value || null })}
                        options={networkOptions}
                      />
                    ) : (
                      <span className="text-foreground-secondary">{networks.data?.find((n) => n.id === t.networkId)?.name ?? "Sem rede"}</span>
                    )}
                  </Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        </Card>
      </div>

      {editing ? <NetworkDialog network={editing === "new" ? undefined : editing} onClose={() => setEditing(undefined)} /> : null}
    </>
  );
}

function NetworkDialog({ network, onClose }: { network?: NetworkRow; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const save = useMutation({
    mutationFn: (body: { name: string; kind: NetworkKind; uf: string | null; municipality: string | null }) =>
      network ? api.put(`/api/networks/${network.id}`, body) : api.post("/api/networks", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["networks"] });
      onClose();
    },
    onError: (err) => setErrors(fieldErrors(err)),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const text = (k: string) => String(d.get(k) ?? "").trim() || null;
    setErrors({});
    save.mutate({ name: text("name") ?? "", kind: String(d.get("kind")) as NetworkKind, uf: text("uf"), municipality: text("municipality") });
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={network ? `Editar ${network.name}` : "Nova rede"}
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="network-form" disabled={save.isPending}>
            Salvar
          </Button>
        </>
      }
    >
      <form id="network-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Nome" name="name" defaultValue={network?.name ?? ""} maxLength={200} error={errors.name} placeholder="Ex.: Rede Estadual do Paraná" />
        <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_6rem]">
          <Select
            label="Tipo"
            name="kind"
            defaultValue={network?.kind ?? "Estadual"}
            options={(Object.keys(kindLabels) as NetworkKind[]).map((k) => ({ value: k, label: kindLabels[k] }))}
          />
          <Input label="UF" name="uf" defaultValue={network?.uf ?? ""} maxLength={2} error={errors.uf} placeholder="PR" />
        </div>
        <Input label="Município (opcional)" name="municipality" defaultValue={network?.municipality ?? ""} maxLength={120} error={errors.municipality} />
      </form>
    </Dialog>
  );
}
