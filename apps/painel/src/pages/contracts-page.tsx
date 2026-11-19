import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, Badge, Button, Card, CardBody, CardHeader, Dialog, EmptyState, format, Input, PageHeader, Select, Table, Td, Textarea, Th, Tr } from "@sinedu/ui";
import { FileText, Layers, Pencil, Plus, Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { billingApi, dateBr, fieldErrors, tierLabel, type Contract, type ContractInput, type ContractStatus, type Plan, type Tier } from "../lib/billing-api";

interface TenantOption {
  id: string;
  slug: string;
  displayName: string;
  status: string;
}

const contractStatus: Record<ContractStatus, { label: string; tone: "good" | "neutral" | "critical" }> = {
  Active: { label: "Vigente", tone: "good" },
  Ended: { label: "Encerrado", tone: "neutral" },
  Cancelled: { label: "Cancelado", tone: "critical" },
};

/** P-06/P-07: planos por faixa de alunos e contrato de cada escola com a APMF pagante. Só SUPER_ADMIN. */
export function ContractsPage() {
  const plans = useQuery({ queryKey: ["billing-plans"], queryFn: billingApi.plans });
  const contracts = useQuery({ queryKey: ["billing-contracts"], queryFn: billingApi.contracts });
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: () => api.get<TenantOption[]>("/api/tenants") });
  const [editingPlan, setEditingPlan] = useState<Plan | "new">();
  const [editingContract, setEditingContract] = useState<Contract | "new">();

  return (
    <>
      <PageHeader
        title="Planos e contratos"
        description="Valor mensal fixo por faixa de alunos ativos. Cada escola tem um contrato com a APMF como pagante; a fatura sai todo mês."
      />

      <div className="grid gap-4 xl:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <Card className="self-start">
          <CardHeader
            title="Planos"
            description="A faixa é escolhida pela contagem de alunos ativos no dia da fatura."
            actions={
              <Button size="sm" onClick={() => setEditingPlan("new")}>
                <Plus /> Novo plano
              </Button>
            }
          />
          <CardBody className="space-y-3">
            {plans.data?.length === 0 ? <EmptyState icon={Layers} title="Nenhum plano" description="Crie um plano com as faixas de alunos e o valor mensal de cada uma." /> : null}
            {plans.data?.map((p) => (
              <div key={p.id} className="rounded-xl border border-border p-4">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="font-medium text-foreground">{p.name}</p>
                  {p.isActive ? null : <Badge tone="neutral">Inativo</Badge>}
                  <span className="text-xs text-muted-foreground">
                    {p.contracts} contrato{p.contracts === 1 ? "" : "s"} vigente{p.contracts === 1 ? "" : "s"}
                  </span>
                  <Button size="sm" variant="ghost" className="ml-auto" onClick={() => setEditingPlan(p)}>
                    <Pencil /> Editar
                  </Button>
                </div>
                <ul className="mt-2 divide-y divide-border text-sm">
                  {p.tiers.map((t, i) => (
                    <li key={i} className="flex justify-between py-1.5">
                      <span className="text-foreground-secondary">{tierLabel(p.tiers, i)}</span>
                      <span className="tabular-nums text-foreground">{format.money(t.monthlyPrice)}/mês</span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </CardBody>
        </Card>

        <Card className="self-start">
          <CardHeader
            title="Contratos"
            description="Um contrato vigente por escola. Encerrado ou cancelado, deixa de gerar faturas."
            actions={
              <Button size="sm" variant="primary" disabled={!plans.data?.some((p) => p.isActive)} onClick={() => setEditingContract("new")}>
                <Plus /> Novo contrato
              </Button>
            }
          />
          {contracts.data?.length === 0 ? (
            <CardBody>
              <EmptyState icon={FileText} title="Nenhum contrato" description="Crie um plano e depois o contrato de cada escola com a APMF." />
            </CardBody>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Escola / pagante</Th>
                  <Th>Plano</Th>
                  <Th>Vigência</Th>
                  <Th>Situação</Th>
                  <Th className="text-right">
                    <span className="sr-only">Ações</span>
                  </Th>
                </tr>
              </thead>
              <tbody>
                {contracts.data?.map((c) => (
                  <Tr key={c.id}>
                    <Td>
                      <p className="font-medium">{c.tenant.name}</p>
                      <p className="text-xs text-muted-foreground">{c.payerName}</p>
                    </Td>
                    <Td className="text-foreground-secondary">{c.plan.name}</Td>
                    <Td className="text-foreground-secondary tabular-nums">
                      {dateBr(c.startsOn)} – {c.endsOn ? dateBr(c.endsOn) : "sem fim"}
                      <p className="text-xs text-muted-foreground">vence dia {c.dueDay}</p>
                    </Td>
                    <Td>
                      <Badge tone={contractStatus[c.status].tone} icon>
                        {contractStatus[c.status].label}
                      </Badge>
                    </Td>
                    <Td className="text-right">
                      <Button size="sm" variant="ghost" onClick={() => setEditingContract(c)}>
                        <Pencil /> Editar
                      </Button>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </Card>
      </div>

      {editingPlan ? <PlanDialog plan={editingPlan === "new" ? undefined : editingPlan} onClose={() => setEditingPlan(undefined)} /> : null}
      {editingContract ? (
        <ContractDialog
          contract={editingContract === "new" ? undefined : editingContract}
          plans={plans.data ?? []}
          tenants={(tenants.data ?? []).filter((t) => t.status === "Active" || (editingContract !== "new" && t.id === editingContract.tenant.id))}
          onClose={() => setEditingContract(undefined)}
        />
      ) : null}
    </>
  );
}

function PlanDialog({ plan, onClose }: { plan?: Plan; onClose: () => void }) {
  const client = useQueryClient();
  const [tiers, setTiers] = useState<{ upTo: string; price: string }[]>(
    plan?.tiers.map((t) => ({ upTo: t.upToStudents == null ? "" : String(t.upToStudents), price: String(t.monthlyPrice) })) ?? [
      { upTo: "300", price: "" },
      { upTo: "", price: "" },
    ],
  );
  const [errors, setErrors] = useState<Record<string, string>>({});
  const save = useMutation({
    mutationFn: (body: { name: string; tiers: Tier[]; isActive: boolean }) => billingApi.savePlan(plan?.id ?? null, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["billing-plans"] });
      onClose();
    },
    onError: (err) => setErrors(fieldErrors(err)),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const parsed = tiers.map((t) => ({ upToStudents: t.upTo.trim() === "" ? null : Number(t.upTo), monthlyPrice: Number(t.price.replace(",", ".")) }));
    if (parsed.some((t) => Number.isNaN(t.monthlyPrice) || (t.upToStudents !== null && !Number.isInteger(t.upToStudents)))) {
      setErrors({ tiers: "Preencha os limites (números inteiros) e os valores de todas as faixas." });
      return;
    }
    setErrors({});
    save.mutate({ name: String(d.get("name") ?? "").trim(), tiers: parsed, isActive: d.get("isActive") === "on" });
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={plan ? `Editar ${plan.name}` : "Novo plano"}
      description="Deixe o limite da última faixa vazio para cobrir “acima de”. Mudanças valem para as próximas faturas."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="plan-form" disabled={save.isPending}>
            Salvar
          </Button>
        </>
      }
    >
      <form id="plan-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Nome do plano" name="name" defaultValue={plan?.name ?? ""} maxLength={120} error={errors.name} />
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium text-foreground">Faixas</legend>
          {tiers.map((t, i) => (
            <div key={i} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] items-end gap-2">
              <Input
                label={i === 0 ? "Até quantos alunos" : `Faixa ${i + 1}: até`}
                inputMode="numeric"
                placeholder="vazio = acima de"
                value={t.upTo}
                onChange={(e) => setTiers(tiers.map((x, j) => (j === i ? { ...x, upTo: e.target.value } : x)))}
              />
              <Input
                label="Valor mensal (R$)"
                inputMode="decimal"
                value={t.price}
                onChange={(e) => setTiers(tiers.map((x, j) => (j === i ? { ...x, price: e.target.value } : x)))}
              />
              <Button type="button" variant="ghost" size="icon" aria-label={`Remover faixa ${i + 1}`} disabled={tiers.length === 1} onClick={() => setTiers(tiers.filter((_, j) => j !== i))}>
                <Trash2 />
              </Button>
            </div>
          ))}
          {errors.tiers ? <p className="text-xs text-critical-text">{errors.tiers}</p> : null}
          <Button type="button" size="sm" variant="ghost" onClick={() => setTiers([...tiers, { upTo: "", price: "" }])}>
            <Plus /> Adicionar faixa
          </Button>
        </fieldset>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" name="isActive" defaultChecked={plan?.isActive ?? true} /> Disponível para novos contratos
        </label>
      </form>
    </Dialog>
  );
}

function ContractDialog({ contract, plans, tenants, onClose }: { contract?: Contract; plans: Plan[]; tenants: TenantOption[]; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const save = useMutation({
    mutationFn: (body: ContractInput) => billingApi.saveContract(contract?.id ?? null, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["billing-contracts"] });
      client.invalidateQueries({ queryKey: ["billing-plans"] });
      client.invalidateQueries({ queryKey: ["billing-alerts"] });
      onClose();
    },
    onError: (err) => setErrors(fieldErrors(err)),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const text = (k: string) => String(d.get(k) ?? "").trim() || null;
    setErrors({});
    save.mutate({
      tenantId: String(d.get("tenantId") ?? ""),
      planId: String(d.get("planId") ?? ""),
      payerName: text("payerName") ?? "",
      payerDocument: text("payerDocument"),
      payerContactName: text("payerContactName"),
      payerEmail: text("payerEmail"),
      payerPhone: text("payerPhone"),
      startsOn: text("startsOn") ?? "",
      endsOn: text("endsOn"),
      dueDay: Number(d.get("dueDay") ?? 10),
      notes: text("notes"),
      status: (String(d.get("status") ?? "Active") as ContractStatus) || "Active",
    });
  };

  const availablePlans = plans.filter((p) => p.isActive || p.id === contract?.plan.id);
  const today = new Date().toISOString().slice(0, 10);

  return (
    <Dialog
      open
      onClose={onClose}
      title={contract ? `Contrato — ${contract.tenant.name}` : "Novo contrato"}
      description="A APMF da escola é a pagante. Os dados aparecem na fatura do mês."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="contract-form" disabled={save.isPending}>
            Salvar
          </Button>
        </>
      }
    >
      <form id="contract-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <div className="grid gap-4 sm:grid-cols-2">
          <Select
            label="Escola"
            name="tenantId"
            defaultValue={contract?.tenant.id}
            options={tenants.map((t) => ({ value: t.id, label: `${t.displayName} (${t.slug})` }))}
            error={errors.tenantId}
          />
          <Select label="Plano" name="planId" defaultValue={contract?.plan.id} options={availablePlans.map((p) => ({ value: p.id, label: p.name }))} error={errors.planId} />
        </div>
        <fieldset className="space-y-4 rounded-xl border border-border p-4">
          <legend className="px-1 text-sm font-medium">Pagante (APMF)</legend>
          <Input label="Nome da APMF" name="payerName" defaultValue={contract?.payerName ?? ""} maxLength={200} error={errors.payerName} placeholder="Ex.: APMF do Colégio Estadual …" />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="CNPJ (opcional)" name="payerDocument" defaultValue={contract?.payerDocument ?? ""} inputMode="numeric" error={errors.payerDocument} />
            <Input label="Responsável (opcional)" name="payerContactName" defaultValue={contract?.payerContactName ?? ""} />
            <Input label="E-mail (opcional)" name="payerEmail" type="email" defaultValue={contract?.payerEmail ?? ""} error={errors.payerEmail} />
            <Input label="Telefone (opcional)" name="payerPhone" defaultValue={contract?.payerPhone ?? ""} />
          </div>
        </fieldset>
        <div className="grid gap-4 sm:grid-cols-3">
          <Input label="Início" name="startsOn" type="date" defaultValue={contract?.startsOn ?? today} error={errors.startsOn} />
          <Input label="Fim (opcional)" name="endsOn" type="date" defaultValue={contract?.endsOn ?? ""} error={errors.endsOn} />
          <Input label="Dia do vencimento" name="dueDay" type="number" min={1} max={28} defaultValue={contract?.dueDay ?? 10} error={errors.dueDay} />
        </div>
        {contract ? (
          <Select
            label="Situação"
            name="status"
            defaultValue={contract.status}
            options={(Object.keys(contractStatus) as ContractStatus[]).map((s) => ({ value: s, label: contractStatus[s].label }))}
          />
        ) : null}
        <Textarea label="Observações (opcional)" name="notes" defaultValue={contract?.notes ?? ""} maxLength={1000} rows={2} />
      </form>
    </Dialog>
  );
}
