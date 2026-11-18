import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, PageHeader, Select, StatTile, Table, Td, Th, Tr } from "@sinedu/ui";
import {
  AlertTriangle, ArrowLeft, Ban, CheckCircle2, ChevronLeft, ChevronRight, CopyCheck, FilePlus2, GitCompareArrows, ListChecks, PencilLine, Sparkles, XCircle,
} from "lucide-react";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { actionLabels, importsApi, type BatchDetails, type ImportField, type ImportRowView, type RowAction } from "../lib/imports-api";

export function ImportPage() {
  const { id = "" } = useParams();
  const [sheet, setSheet] = useState<string>();
  const [headerRow, setHeaderRow] = useState<number>();
  const [editingMapping, setEditingMapping] = useState(false);
  const batch = useQuery({ queryKey: ["import", id, sheet, headerRow], queryFn: () => importsApi.get(id, sheet, headerRow), placeholderData: keepPreviousData });

  if (batch.error instanceof ApiError && batch.error.status === 404) return <Alert tone="warning" title="Importação não encontrada." />;
  const b = batch.data;

  const step = !b ? null : b.status === "Completed" ? "result" : b.status === "Validated" && !editingMapping ? "review" : b.status === "Cancelled" ? "cancelled" : "mapping";

  return (
    <>
      <PageHeader
        eyebrow={
          <Link to="/importacoes" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" aria-hidden /> Importações
          </Link>
        }
        title={b?.fileName ?? "Importação"}
        description={b ? `Enviado em ${new Date(b.createdAt).toLocaleString("pt-BR")}` : undefined}
      />

      <Steps current={step} />

      {b && step === "mapping" ? (
        <MappingStep
          batch={b}
          onSheet={(s) => {
            setSheet(s);
            setHeaderRow(undefined);
          }}
          onHeaderRow={setHeaderRow}
          onValidated={() => setEditingMapping(false)}
        />
      ) : null}
      {b && step === "review" ? <ReviewStep batch={b} onEditMapping={() => setEditingMapping(true)} /> : null}
      {b && step === "result" ? <ResultStep batch={b} /> : null}
      {step === "cancelled" ? <Alert tone="info" title="Importação cancelada. Nenhum dado foi gravado." /> : null}
    </>
  );
}

function Steps({ current }: { current: string | null }) {
  const steps = [
    { key: "mapping", label: "Mapeamento" },
    { key: "review", label: "Prévia e revisão" },
    { key: "result", label: "Resultado" },
  ];
  const index = steps.findIndex((s) => s.key === current);
  return (
    <ol className="mb-6 flex flex-wrap items-center gap-2 text-sm">
      {steps.map((s, i) => (
        <li key={s.key} className="flex items-center gap-2">
          <span
            className={`grid size-6 place-items-center rounded-full text-xs font-semibold ${
              i < index ? "bg-good-soft text-good-text" : i === index ? "bg-primary text-primary-foreground" : "bg-surface-sunken text-muted-foreground"
            }`}
          >
            {i < index ? <CheckCircle2 className="size-3.5" aria-hidden /> : i + 1}
          </span>
          <span className={i === index ? "font-medium text-foreground" : "text-muted-foreground"}>{s.label}</span>
          {i < steps.length - 1 ? <span className="mx-1 h-px w-8 bg-border-strong" aria-hidden /> : null}
        </li>
      ))}
    </ol>
  );
}

// ---------- Mapeamento ----------

function MappingStep({ batch, onSheet, onHeaderRow, onValidated }: { batch: BatchDetails; onSheet: (s: string) => void; onHeaderRow: (n: number) => void; onValidated: () => void }) {
  const client = useQueryClient();
  const fields = useQuery({ queryKey: ["import-fields"], queryFn: importsApi.fields });
  const [overrides, setOverrides] = useState<Partial<Record<ImportField, number | null>>>({});
  const mapping = Object.fromEntries(
    Object.entries({ ...batch.mapping, ...overrides }).filter(([, v]) => v !== null && v !== undefined),
  ) as Partial<Record<ImportField, number>>;

  const validate = useMutation({
    mutationFn: () => importsApi.map(batch.id, batch.sheet ?? "", batch.headerRow, mapping),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["import", batch.id] });
      onValidated();
    },
  });

  const columnLabel = (i: number) => {
    const header = batch.headers[i];
    return `Coluna ${i + 1}${header ? ` — ${header}` : " (sem título)"}`;
  };
  const columnOptions = [{ value: "", label: "Não importar" }, ...batch.headers.map((_, i) => ({ value: String(i), label: columnLabel(i) }))];
  const missing = (fields.data ?? []).filter((f) => f.required && mapping[f.field] === undefined);

  return (
    <div className="grid gap-4 xl:grid-cols-3">
      <Card className="xl:col-span-2">
        <CardHeader
          title="Ligue as colunas da planilha aos campos do Sinedu"
          description="Sugerimos o mapeamento pelo cabeçalho e pelo conteúdo. Confira antes de continuar."
          actions={
            <Badge tone="brand">
              <Sparkles aria-hidden />
              Sugestão automática
            </Badge>
          }
        />
        <CardBody>
          <div className="mb-5 grid gap-4 sm:grid-cols-2">
            <Select
              label="Aba da planilha"
              disabled={batch.status === "Validated"}
              value={batch.sheet ?? ""}
              onChange={(e) => onSheet(e.target.value)}
              options={batch.sheets.map((s) => ({ value: s.name, label: `${s.name} (${s.rowCount} linhas)` }))}
            />
            <Select
              label="Linha do cabeçalho"
              value={String(batch.headerRow)}
              onChange={(e) => onHeaderRow(Number(e.target.value))}
              options={Array.from({ length: 10 }, (_, i) => ({ value: String(i + 1), label: `Linha ${i + 1}` }))}
            />
          </div>
          <ul className="divide-y divide-border">
            {fields.data?.map((f) => {
              const col = mapping[f.field];
              const samples = col === undefined ? [] : batch.sample.map((r) => r[col]).filter(Boolean).slice(0, 2);
              return (
                <li key={f.field} className="grid items-center gap-2 py-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-foreground">
                      {f.label}
                      {f.required ? <span className="ml-1 text-critical-text">*</span> : null}
                    </p>
                    {f.hint ? <p className="text-xs text-muted-foreground">{f.hint}</p> : null}
                  </div>
                  <div className="min-w-0">
                    <Select
                      label={f.label}
                      hideLabel
                      value={col === undefined ? "" : String(col)}
                      onChange={(e) => setOverrides((o) => ({ ...o, [f.field]: e.target.value === "" ? null : Number(e.target.value) }))}
                      options={columnOptions}
                    />
                    {samples.length ? <p className="mt-1 truncate text-xs text-muted-foreground">Ex.: {samples.join(" · ")}</p> : null}
                  </div>
                </li>
              );
            })}
          </ul>
        </CardBody>
      </Card>

      <div className="space-y-4">
        <Card>
          <CardHeader title="Validar e ver a prévia" description="Nada é gravado nesta etapa." />
          <CardBody className="space-y-3">
            {missing.length ? <Alert tone="warning" title={`Falta mapear: ${missing.map((m) => m.label).join(", ")}.`} /> : null}
            {mapping.Code === undefined ? (
              <Alert tone="info" title="Sem CGM, alunos com o mesmo nome viram possíveis duplicados para você revisar." />
            ) : null}
            {validate.error ? <Alert tone="critical" title={validate.error instanceof ApiError ? validate.error.message : "Não foi possível validar."} /> : null}
            <Button variant="primary" size="lg" className="w-full" disabled={missing.length > 0 || validate.isPending} onClick={() => validate.mutate()}>
              <ListChecks />
              {validate.isPending ? "Validando…" : "Validar e ver prévia"}
            </Button>
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="Prévia da aba" description="Primeiras linhas, como vieram." />
          <div className="mt-3 max-h-72 overflow-auto px-5 pb-5 text-xs">
            {batch.sample.map((row, i) => (
              <p key={i} className="truncate border-b border-border py-1.5 text-foreground-secondary last:border-0">
                {row.filter(Boolean).slice(0, 5).join(" · ")}
              </p>
            ))}
          </div>
        </Card>
      </div>
    </div>
  );
}

// ---------- Prévia e revisão ----------

const filters: { key: RowAction | "pending" | "all"; label: string }[] = [
  { key: "pending", label: "Para revisar" },
  { key: "all", label: "Todas" },
  { key: "New", label: "Novos" },
  { key: "Update", label: "Atualizações" },
  { key: "Rejected", label: "Rejeitados" },
  { key: "Unchanged", label: "Sem alteração" },
];

function ReviewStep({ batch, onEditMapping }: { batch: BatchDetails; onEditMapping: () => void }) {
  const client = useQueryClient();
  const navigate = useNavigate();
  const t = batch.totals!;
  const [filter, setFilter] = useState<(typeof filters)[number]["key"]>(t.pending > 0 ? "pending" : "all");
  const [page, setPage] = useState(1);
  const [confirming, setConfirming] = useState(false);

  const rows = useQuery({
    queryKey: ["import-rows", batch.id, filter, page],
    queryFn: () =>
      importsApi.rows(batch.id, {
        pendingOnly: filter === "pending",
        action: filter === "pending" || filter === "all" ? undefined : filter,
        page,
      }),
    placeholderData: keepPreviousData,
  });
  const pendingNow = useQuery({ queryKey: ["import-rows", batch.id, "pending-count"], queryFn: () => importsApi.rows(batch.id, { pendingOnly: true }) });
  const refresh = () => client.invalidateQueries({ queryKey: ["import-rows", batch.id] });

  const skipAll = useMutation({ mutationFn: () => importsApi.skipPending(batch.id), onSuccess: refresh });
  const cancel = useMutation({ mutationFn: () => importsApi.cancel(batch.id), onSuccess: () => navigate("/importacoes") });
  const confirm = useMutation({
    mutationFn: () => importsApi.confirm(batch.id),
    onSuccess: () => {
      setConfirming(false);
      client.invalidateQueries();
    },
  });

  const pending = pendingNow.data?.total ?? t.pending;
  const toApply = t.new + t.update;
  const pages = rows.data ? Math.max(1, Math.ceil(rows.data.total / rows.data.pageSize)) : 1;

  return (
    <>
      <section className="grid gap-3 sm:grid-cols-3 xl:grid-cols-6">
        <StatTile label="Novos" value={t.new} icon={FilePlus2} />
        <StatTile label="Atualizações" value={t.update} icon={PencilLine} />
        <StatTile label="Sem alteração" value={t.unchanged} icon={CopyCheck} />
        <StatTile label="Possíveis duplicados" value={t.possibleDuplicate} icon={GitCompareArrows} />
        <StatTile label="Conflitos" value={t.conflict} icon={AlertTriangle} />
        <StatTile label="Rejeitados" value={t.rejected} icon={XCircle} />
      </section>
      {t.newClassrooms > 0 ? <Alert tone="info" className="mt-3" title={`${t.newClassrooms} turma(s) nova(s) serão criadas no ano letivo ativo.`} /> : null}

      <Card className="mt-4">
        <div className="flex flex-wrap items-center justify-between gap-3 px-5 pt-5">
          <div role="tablist" className="inline-flex flex-wrap gap-1 rounded-lg border border-border bg-surface-sunken p-0.5">
            {filters.map((f) => (
              <button
                key={f.key}
                type="button"
                role="tab"
                aria-selected={filter === f.key}
                onClick={() => {
                  setFilter(f.key);
                  setPage(1);
                }}
                className={`rounded-md px-2.5 py-1 text-xs font-medium transition-colors ${filter === f.key ? "bg-surface-raised text-foreground shadow-card" : "text-muted-foreground hover:text-foreground"}`}
              >
                {f.label}
                {f.key === "pending" && pending > 0 ? <span className="ml-1 rounded-full bg-warning-soft px-1.5 text-warning-text">{pending}</span> : null}
              </button>
            ))}
          </div>
          {pending > 0 ? (
            <Button size="sm" variant="ghost" onClick={() => skipAll.mutate()} disabled={skipAll.isPending}>
              <Ban />
              Ignorar todas as pendentes
            </Button>
          ) : null}
        </div>
        <div className="mt-4">
          <Table>
            <thead>
              <tr>
                <Th align="right">Linha</Th>
                <Th>Aluno</Th>
                <Th>Turma</Th>
                <Th>Situação</Th>
                <Th>Detalhes</Th>
              </tr>
            </thead>
            <tbody>
              {rows.data?.items.map((r) => (
                <ReviewRow key={r.id} batchId={batch.id} row={r} onDecided={refresh} />
              ))}
              {rows.data?.items.length === 0 ? (
                <Tr>
                  <Td colSpan={5} className="py-8 text-center text-muted-foreground">
                    {filter === "pending" ? "Nada para revisar." : "Nenhuma linha neste filtro."}
                  </Td>
                </Tr>
              ) : null}
            </tbody>
          </Table>
        </div>
        {pages > 1 ? (
          <div className="flex items-center justify-between border-t border-border px-5 py-3 text-sm text-muted-foreground">
            <span>
              Página {page} de {pages}
            </span>
            <div className="flex gap-2">
              <Button size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                <ChevronLeft />
              </Button>
              <Button size="sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                <ChevronRight />
              </Button>
            </div>
          </div>
        ) : null}
      </Card>

      <div className="sticky bottom-4 mt-4 flex flex-wrap items-center justify-between gap-3 rounded-(--radius-card) border border-border bg-surface/95 p-4 shadow-pop backdrop-blur">
        <div className="flex gap-2">
          <Button onClick={onEditMapping}>Voltar ao mapeamento</Button>
          <Button variant="ghost" onClick={() => cancel.mutate()}>
            Cancelar importação
          </Button>
        </div>
        <div className="flex items-center gap-3">
          {pending > 0 ? <span className="text-sm text-warning-text">Revise {pending} linha(s) para confirmar.</span> : null}
          <Button variant="primary" size="lg" disabled={pending > 0} onClick={() => setConfirming(true)}>
            <CheckCircle2 />
            Confirmar importação
          </Button>
        </div>
      </div>

      <Dialog
        open={confirming}
        onClose={() => setConfirming(false)}
        title="Confirmar importação?"
        description={`Serão gravados até ${toApply} aluno(s) (${t.new} novos, ${t.update} atualizações). Linhas rejeitadas ou ignoradas não entram.`}
        footer={
          <>
            <Button onClick={() => setConfirming(false)}>Voltar</Button>
            <Button variant="primary" disabled={confirm.isPending} onClick={() => confirm.mutate()}>
              {confirm.isPending ? "Gravando…" : "Confirmar"}
            </Button>
          </>
        }
      >
        {confirm.error ? <Alert tone="critical" title={confirm.error instanceof ApiError ? confirm.error.message : "Falhou."} /> : null}
        <p className="text-sm text-foreground-secondary">A importação fica registrada na auditoria com o seu nome. Células vazias nunca apagam dados existentes.</p>
      </Dialog>
    </>
  );
}

const actionTone: Record<RowAction, "good" | "info" | "warning" | "critical" | "neutral" | "brand"> = {
  Pending: "neutral",
  Empty: "neutral",
  New: "good",
  Update: "info",
  Unchanged: "neutral",
  PossibleDuplicate: "warning",
  Conflict: "warning",
  Rejected: "critical",
};

function ReviewRow({ batchId, row: r, onDecided }: { batchId: string; row: ImportRowView; onDecided: () => void }) {
  const decide = useMutation({
    mutationFn: ({ decision, target }: { decision: "Create" | "Update" | "Skip"; target?: string }) => importsApi.decide(batchId, r.id, decision, target),
    onSuccess: onDecided,
  });
  const needsDecision = (r.action === "PossibleDuplicate" || r.action === "Conflict") && r.decision === "None";
  const decided = r.decision !== "None";

  return (
    <Tr>
      <Td align="right" className="w-16 align-top text-muted-foreground">{r.rowNumber}</Td>
      <Td className="align-top">
        <p className="font-medium">{r.name ?? "—"}</p>
        <p className="font-mono text-xs text-muted-foreground">{r.code ?? "sem CGM"}</p>
      </Td>
      <Td className="align-top text-foreground-secondary">{r.classroom ?? "—"}</Td>
      <Td className="align-top">
        <Badge tone={decided ? "neutral" : actionTone[r.action]}>{decided ? decisionLabel(r.decision) : actionLabels[r.action]}</Badge>
      </Td>
      <Td className="max-w-md whitespace-normal align-top">
        <div className="space-y-1.5 text-xs">
          {r.errors.map((e) => (
            <p key={e} className="text-critical-text">{e}</p>
          ))}
          {r.warnings.map((w) => (
            <p key={w} className="text-warning-text">{w}</p>
          ))}
          {r.changes.length ? <p className="text-foreground-secondary">Muda: {r.changes.join(", ")}</p> : null}

          {needsDecision && r.action === "PossibleDuplicate" ? (
            <div className="space-y-1.5">
              {r.candidates.map((c) => (
                <div key={c.id} className="flex flex-wrap items-center gap-2 rounded-lg border border-border p-2">
                  <span className="text-foreground">
                    {c.name} · {c.classroom ?? "sem turma"} · {c.code ?? "sem CGM"}
                  </span>
                  <Button size="sm" className="h-7" onClick={() => decide.mutate({ decision: "Update", target: c.id })}>
                    É este aluno
                  </Button>
                </div>
              ))}
              <div className="flex gap-2">
                <Button size="sm" className="h-7" onClick={() => decide.mutate({ decision: "Create" })}>
                  É outro aluno
                </Button>
                <Button size="sm" variant="ghost" className="h-7" onClick={() => decide.mutate({ decision: "Skip" })}>
                  Ignorar linha
                </Button>
              </div>
            </div>
          ) : null}

          {needsDecision && r.action === "Conflict" ? (
            <div className="flex flex-wrap gap-2">
              {r.existing ? (
                <Button size="sm" className="h-7" onClick={() => decide.mutate({ decision: "Update", target: r.existing!.id })}>
                  Atualizar {r.existing.name}
                </Button>
              ) : null}
              <Button size="sm" variant="ghost" className="h-7" onClick={() => decide.mutate({ decision: "Skip" })}>
                Ignorar linha
              </Button>
            </div>
          ) : null}
          {decide.error ? <p className="text-critical-text">{decide.error instanceof ApiError ? decide.error.message : "Falhou."}</p> : null}
        </div>
      </Td>
    </Tr>
  );
}

function decisionLabel(d: string) {
  return d === "Skip" ? "Será ignorada" : d === "Create" ? "Criar novo" : d === "Update" ? "Atualizar existente" : d;
}

// ---------- Resultado ----------

function ResultStep({ batch }: { batch: BatchDetails }) {
  const t = batch.totals!;
  return (
    <Card className="p-6">
      <div className="flex items-start gap-4">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-good-soft text-good-text">
          <CheckCircle2 className="size-6" aria-hidden />
        </span>
        <div className="flex-1">
          <h2 className="text-lg font-semibold text-foreground">Importação concluída</h2>
          <p className="text-sm text-muted-foreground">
            {batch.completedAt ? `Em ${new Date(batch.completedAt).toLocaleString("pt-BR")}. ` : ""}Registrada na auditoria.
          </p>
          <dl className="mt-5 grid gap-3 sm:grid-cols-5">
            {[
              ["Criados", t.created],
              ["Atualizados", t.updated],
              ["Sem alteração", t.unchanged],
              ["Ignorados", t.skipped],
              ["Falhas", t.failed],
            ].map(([label, value]) => (
              <div key={label} className="rounded-xl bg-surface-sunken px-4 py-3">
                <dt className="text-xs text-muted-foreground">{label}</dt>
                <dd className="text-2xl font-semibold text-foreground">{value}</dd>
              </div>
            ))}
          </dl>
          <div className="mt-5 flex gap-2">
            <Link to="/alunos">
              <Button variant="primary">Ver alunos</Button>
            </Link>
            <Link to="/turmas">
              <Button>Ver turmas</Button>
            </Link>
          </div>
        </div>
      </div>
    </Card>
  );
}
