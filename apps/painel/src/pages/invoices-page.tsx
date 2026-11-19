import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, CardBody, CardHeader, Dialog, EmptyState, format, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { BellRing, CircleDollarSign, FileCheck2, Receipt, RefreshCw, XCircle } from "lucide-react";
import { useState, type FormEvent } from "react";
import { useIsSuperAdmin } from "../lib/session";
import { alertLabels, billingApi, currentMonth, dateBr, fieldErrors, monthBr, type BillingAlert, type Invoice, type InvoiceStatus } from "../lib/billing-api";

const invoiceStatus: Record<InvoiceStatus, { label: string; tone: "info" | "critical" | "good" | "neutral" }> = {
  Open: { label: "Em aberto", tone: "info" },
  Overdue: { label: "Vencida", tone: "critical" },
  Paid: { label: "Paga", tone: "good" },
  Cancelled: { label: "Cancelada", tone: "neutral" },
};

const alertTone: Record<BillingAlert["kind"], "critical" | "warning"> = {
  fatura_vencida: "critical",
  contrato_vencido: "critical",
  contrato_vencendo: "warning",
  sem_contrato: "warning",
  limite_excedido: "warning",
};

/** Últimos 12 meses para o filtro. */
function recentMonths() {
  const now = new Date();
  return Array.from({ length: 12 }, (_, i) => {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
  });
}

/**
 * P-08/P-09: faturas mensais com baixa manual e NF opcional, e alertas de cobrança. A suspensão de uma escola
 * continua sendo decisão manual em Escolas (D16). SUPORTE vê só os alertas.
 */
export function InvoicesPage() {
  const superAdmin = useIsSuperAdmin();
  const client = useQueryClient();
  const [month, setMonth] = useState(currentMonth());
  const [status, setStatus] = useState("");
  const [paying, setPaying] = useState<Invoice>();
  const [noting, setNoting] = useState<Invoice>();
  const [cancelling, setCancelling] = useState<Invoice>();

  const alerts = useQuery({ queryKey: ["billing-alerts"], queryFn: billingApi.alerts });
  const invoices = useQuery({
    queryKey: ["billing-invoices", month, status],
    queryFn: () => billingApi.invoices({ month: status === "Overdue" ? undefined : month, status: status === "Overdue" ? undefined : status || undefined, overdue: status === "Overdue" }),
    enabled: superAdmin,
  });
  const generate = useMutation({
    mutationFn: () => billingApi.generate(month),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["billing-invoices"] });
      client.invalidateQueries({ queryKey: ["billing-alerts"] });
    },
  });

  const rows = invoices.data ?? [];
  const totals = {
    billed: rows.filter((i) => i.status !== "Cancelled").reduce((s, i) => s + i.amount, 0),
    received: rows.filter((i) => i.status === "Paid").reduce((s, i) => s + (i.paidAmount ?? 0), 0),
    open: rows.filter((i) => i.status === "Open" || i.status === "Overdue").reduce((s, i) => s + i.amount, 0),
  };

  return (
    <>
      <PageHeader
        title="Faturas"
        description="Uma fatura por escola por mês, com o valor da faixa do plano. Baixa manual; nota fiscal opcional."
        actions={
          superAdmin ? (
            <Button variant="primary" disabled={generate.isPending} onClick={() => generate.mutate()}>
              <RefreshCw /> {generate.isPending ? "Gerando…" : `Gerar faturas de ${monthBr(month)}`}
            </Button>
          ) : null
        }
      />

      {generate.data ? (
        <Alert tone={generate.data.skippedWithoutCount.length ? "warning" : "good"} title={`${generate.data.created} fatura(s) gerada(s) para ${monthBr(month)}.`} className="mb-4">
          {generate.data.alreadyExisted ? `${generate.data.alreadyExisted} já existia(m). ` : ""}
          {generate.data.skippedWithoutCount.length
            ? `Sem contagem de alunos ainda (rode a coleta em Visão geral): ${generate.data.skippedWithoutCount.join(", ")}.`
            : ""}
        </Alert>
      ) : null}

      <Card className="mb-4">
        <CardHeader title="Alertas de cobrança" description="Atualizados a cada visita. Suspender uma escola é sempre decisão manual." />
        <CardBody>
          {alerts.data?.length === 0 ? (
            <p className="flex items-center gap-2 text-sm text-good-text">
              <FileCheck2 className="size-4" aria-hidden /> Nada pendente.
            </p>
          ) : (
            <ul className="space-y-2">
              {alerts.data?.map((a, i) => (
                <li key={`${a.kind}-${a.tenantId}-${a.invoiceId ?? i}`} className="flex flex-wrap items-center gap-2 text-sm">
                  <Badge tone={alertTone[a.kind]} icon>
                    {alertLabels[a.kind]}
                  </Badge>
                  <span className="font-medium text-foreground">{a.school}</span>
                  <span className="text-foreground-secondary">{a.message}</span>
                </li>
              ))}
            </ul>
          )}
        </CardBody>
      </Card>

      {superAdmin ? (
        <Card>
          <CardHeader
            title="Faturas do período"
            actions={
              <div className="flex flex-wrap items-end gap-2">
                <Select label="Mês" hideLabel className="w-36" value={month} onChange={(e) => setMonth(e.target.value)} options={recentMonths().map((m) => ({ value: m, label: monthBr(m) }))} />
                <Select
                  label="Situação"
                  hideLabel
                  className="w-44"
                  value={status}
                  onChange={(e) => setStatus(e.target.value)}
                  options={[
                    { value: "", label: "Todas" },
                    { value: "Open", label: "Em aberto" },
                    { value: "Overdue", label: "Vencidas (todos os meses)" },
                    { value: "Paid", label: "Pagas" },
                    { value: "Cancelled", label: "Canceladas" },
                  ]}
                />
              </div>
            }
          />
          <CardBody className="grid gap-3 pb-0 sm:grid-cols-3">
            <Total label="Faturado" value={totals.billed} />
            <Total label="Recebido" value={totals.received} />
            <Total label="A receber" value={totals.open} />
          </CardBody>
          {rows.length === 0 ? (
            <CardBody>
              <EmptyState icon={Receipt} title="Nenhuma fatura" description="As faturas do mês saem sozinhas para cada contrato vigente. Você também pode gerá-las no botão acima." />
            </CardBody>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Escola / pagante</Th>
                  <Th>Referência</Th>
                  <Th>Faixa</Th>
                  <Th className="text-right">Valor</Th>
                  <Th>Vencimento</Th>
                  <Th>Situação</Th>
                  <Th className="text-right">
                    <span className="sr-only">Ações</span>
                  </Th>
                </tr>
              </thead>
              <tbody>
                {rows.map((i) => (
                  <Tr key={i.id}>
                    <Td>
                      <p className="font-medium">{i.school}</p>
                      <p className="text-xs text-muted-foreground">{i.payer}</p>
                    </Td>
                    <Td className="tabular-nums text-foreground-secondary">{monthBr(i.referenceMonth)}</Td>
                    <Td className="text-foreground-secondary">
                      {i.tierLabel}
                      <p className="text-xs text-muted-foreground tabular-nums">{format.integer(i.students)} alunos na emissão</p>
                    </Td>
                    <Td className="text-right tabular-nums">{format.money(i.amount)}</Td>
                    <Td className="tabular-nums text-foreground-secondary">{dateBr(i.dueOn)}</Td>
                    <Td>
                      <Badge tone={invoiceStatus[i.status].tone} icon>
                        {invoiceStatus[i.status].label}
                      </Badge>
                      {i.status === "Paid" ? <p className="mt-1 text-xs text-muted-foreground">em {dateBr(i.paidOn)}</p> : null}
                      {i.invoiceNumber ? <p className="mt-1 text-xs text-muted-foreground">NF {i.invoiceNumber}</p> : null}
                    </Td>
                    <Td className="text-right">
                      <div className="flex justify-end gap-1">
                        {i.status === "Open" || i.status === "Overdue" ? (
                          <Button size="sm" onClick={() => setPaying(i)}>
                            <CircleDollarSign /> Baixa
                          </Button>
                        ) : null}
                        {i.status !== "Cancelled" ? (
                          <Button size="sm" variant="ghost" onClick={() => setNoting(i)}>
                            NF
                          </Button>
                        ) : null}
                        {i.status === "Open" || i.status === "Overdue" ? (
                          <Button size="sm" variant="ghost" aria-label={`Cancelar fatura de ${i.school}`} onClick={() => setCancelling(i)}>
                            <XCircle />
                          </Button>
                        ) : null}
                      </div>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </Card>
      ) : (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <BellRing className="size-4" aria-hidden /> Faturas e contratos são geridos pela administração da plataforma.
        </p>
      )}

      {paying ? <PayDialog invoice={paying} onClose={() => setPaying(undefined)} /> : null}
      {noting ? <FiscalNoteDialog invoice={noting} onClose={() => setNoting(undefined)} /> : null}
      {cancelling ? <CancelDialog invoice={cancelling} onClose={() => setCancelling(undefined)} /> : null}
    </>
  );
}

function Total({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border bg-surface-raised px-4 py-3">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="mt-0.5 text-lg font-semibold tabular-nums text-foreground">{format.money(value)}</p>
    </div>
  );
}

function useInvoiceAction(onClose: () => void) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const done = () => {
    client.invalidateQueries({ queryKey: ["billing-invoices"] });
    client.invalidateQueries({ queryKey: ["billing-alerts"] });
    onClose();
  };
  return { errors, setErrors, done };
}

function PayDialog({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const { errors, setErrors, done } = useInvoiceAction(onClose);
  const pay = useMutation({ mutationFn: (body: Parameters<typeof billingApi.pay>[1]) => billingApi.pay(invoice.id, body), onSuccess: done, onError: (e) => setErrors(fieldErrors(e)) });
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const text = (k: string) => String(d.get(k) ?? "").trim() || null;
    pay.mutate({
      paidOn: text("paidOn") ?? "",
      paidAmount: Number(String(d.get("paidAmount") ?? "").replace(",", ".")),
      invoiceNumber: text("invoiceNumber"),
      issuedOn: text("issuedOn"),
      notes: text("notes"),
    });
  };
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Baixa — ${invoice.school}`}
      description={`${monthBr(invoice.referenceMonth)} · ${format.money(invoice.amount)} · vencimento ${dateBr(invoice.dueOn)}`}
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="pay-form" disabled={pay.isPending}>
            Registrar pagamento
          </Button>
        </>
      }
    >
      <form id="pay-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Pago em" name="paidOn" type="date" defaultValue={new Date().toISOString().slice(0, 10)} />
          <Input label="Valor recebido (R$)" name="paidAmount" inputMode="decimal" defaultValue={String(invoice.amount).replace(".", ",")} error={errors.paidAmount} />
          <Input label="Nº da nota fiscal (opcional)" name="invoiceNumber" defaultValue={invoice.invoiceNumber ?? ""} maxLength={60} />
          <Input label="Emitida em (opcional)" name="issuedOn" type="date" defaultValue={invoice.issuedOn ?? ""} />
        </div>
        <Input label="Observação (opcional)" name="notes" maxLength={500} placeholder="Ex.: transferência da APMF" />
      </form>
    </Dialog>
  );
}

function FiscalNoteDialog({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const { errors, setErrors, done } = useInvoiceAction(onClose);
  const save = useMutation({ mutationFn: (body: Parameters<typeof billingApi.fiscalNote>[1]) => billingApi.fiscalNote(invoice.id, body), onSuccess: done, onError: (e) => setErrors(fieldErrors(e)) });
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Nota fiscal — ${invoice.school}`}
      description="Registro opcional. Deixe o número em branco para remover."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="nf-form" disabled={save.isPending}>
            Salvar
          </Button>
        </>
      }
    >
      <form
        id="nf-form"
        className="grid gap-4 sm:grid-cols-2"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          const d = new FormData(e.currentTarget);
          save.mutate({ invoiceNumber: String(d.get("invoiceNumber") ?? "").trim() || null, issuedOn: String(d.get("issuedOn") ?? "") || null });
        }}
      >
        {errors.form ? <Alert tone="critical" title={errors.form} className="sm:col-span-2" /> : null}
        <Input label="Número da NF" name="invoiceNumber" defaultValue={invoice.invoiceNumber ?? ""} maxLength={60} error={errors.invoiceNumber} />
        <Input label="Emitida em" name="issuedOn" type="date" defaultValue={invoice.issuedOn ?? ""} />
      </form>
    </Dialog>
  );
}

function CancelDialog({ invoice, onClose }: { invoice: Invoice; onClose: () => void }) {
  const { errors, setErrors, done } = useInvoiceAction(onClose);
  const cancel = useMutation({ mutationFn: (reason: string) => billingApi.cancel(invoice.id, reason), onSuccess: done, onError: (e) => setErrors(fieldErrors(e)) });
  return (
    <Dialog
      open
      onClose={onClose}
      title={`Cancelar fatura — ${invoice.school}`}
      description={`${monthBr(invoice.referenceMonth)} · ${format.money(invoice.amount)}. Depois de cancelada, a fatura do mês pode ser gerada de novo.`}
      footer={
        <>
          <Button onClick={onClose}>Voltar</Button>
          <Button variant="danger" type="submit" form="cancel-form" disabled={cancel.isPending}>
            Cancelar fatura
          </Button>
        </>
      }
    >
      <form
        id="cancel-form"
        className="space-y-4"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          cancel.mutate(String(new FormData(e.currentTarget).get("reason") ?? "").trim());
        }}
      >
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Motivo" name="reason" maxLength={500} error={errors.reason} placeholder="Ex.: valor corrigido após recontagem" />
      </form>
    </Dialog>
  );
}
