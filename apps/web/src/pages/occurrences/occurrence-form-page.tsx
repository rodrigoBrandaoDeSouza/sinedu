import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, Input, PageHeader, Select, Textarea } from "@sinedu/ui";
import { ArrowLeft, Search, ShieldAlert } from "lucide-react";
import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router";
import { fromLocalInput, occurrencesApi, toLocalInput, type OccurrenceDetails, type OccurrenceInput } from "../../lib/occurrences-api";
import { schoolApi, type StudentRow } from "../../lib/school-api";
import { errorsOf } from "../protected/utils";

type Picked = { id: string; name: string; code: string | null; classroom: string | null };

/** Nova ocorrência (/ocorrencias/nova?aluno=) ou edição (/ocorrencias/:id/editar). */
export function OccurrenceFormPage() {
  const { id } = useParams();
  const existing = useQuery({ queryKey: ["occurrence", id], queryFn: () => occurrencesApi.get(id!), enabled: !!id });
  if (id && !existing.data) {
    return existing.error ? <Alert tone="warning" title="Ocorrência não encontrada." /> : <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" />;
  }

  return <OccurrenceForm key={id ?? "new"} existing={existing.data} />;
}

function OccurrenceForm({ existing }: { existing?: OccurrenceDetails }) {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const formal = existing !== undefined && existing.status !== "Rascunho";
  const options = useQuery({ queryKey: ["occurrence-categories"], queryFn: occurrencesApi.categories });
  // undefined = ainda vale o aluno da URL (?aluno=); null = a pessoa limpou a escolha.
  const [picked, setPicked] = useState<Picked | null | undefined>(
    existing ? { id: existing.studentId, name: existing.studentName, code: existing.studentCode, classroom: existing.classroom } : undefined,
  );
  const preselected = params.get("aluno");
  const pre = useQuery({ queryKey: ["student", preselected], queryFn: () => schoolApi.student(preselected!), enabled: !existing && !!preselected });
  const student: Picked | null =
    picked !== undefined
      ? picked
      : pre.data
        ? { id: pre.data.id, name: pre.data.socialName ?? pre.data.fullName, code: pre.data.institutionalCode, classroom: pre.data.classroom?.name ?? null }
        : null;
  const setStudent = (value: Picked | null) => setPicked(value);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: async ({ input, register, reason }: { input: OccurrenceInput; register: boolean; reason: string | null }) => {
      if (!existing) return (await occurrencesApi.create(input, register)).id;
      await occurrencesApi.update(existing.id, input, reason, existing.version);
      if (register && existing.status === "Rascunho") await occurrencesApi.register(existing.id, existing.version);
      return existing.id;
    },
    onSuccess: (occurrenceId) => navigate(`/ocorrencias/${occurrenceId}`),
    onError: (err) => setErrors(errorsOf(err)),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const register = (e.nativeEvent as SubmitEvent).submitter?.getAttribute("value") === "register";
    const d = new FormData(e.currentTarget);
    const text = (k: string) => String(d.get(k) ?? "").trim() || null;
    setErrors({});
    if (!student) return setErrors({ studentId: "Escolha o aluno." });
    const when = text("occurredAt");
    if (!when) return setErrors({ occurredAt: "Informe a data e a hora do fato." });
    save.mutate({
      register,
      reason: text("reason"),
      input: {
        studentId: student.id,
        occurredAt: fromLocalInput(when),
        categoryId: String(d.get("categoryId") ?? ""),
        severityId: text("severityId"),
        location: text("location"),
        description: text("description") ?? "",
        involved: text("involved"),
        actionsTaken: text("actionsTaken"),
        referrals: text("referrals"),
        notes: text("notes"),
      },
    });
  };

  const categories = options.data?.categories.filter((c) => c.isActive || c.id === existing?.categoryId) ?? [];
  return (
    <>
      <Link to={existing ? `/ocorrencias/${existing.id}` : "/ocorrencias"} className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
        <ArrowLeft className="size-4" aria-hidden /> {existing ? "Ocorrência" : "Ocorrências"}
      </Link>
      <PageHeader title={existing ? "Editar ocorrência" : "Nova ocorrência"} description="Descreva o fato de forma objetiva: o que aconteceu, onde e quando." />

      <Alert tone="warning" className="mb-4" title="Violência, abuso ou proteção não entram aqui">
        <span className="inline-flex items-center gap-1">
          <ShieldAlert className="size-4" aria-hidden />
          Use a aba Proteção/Sigilo do aluno. Ocorrências são vistas por toda a equipe pedagógica e entram nos indicadores.
        </span>
      </Alert>

      <form onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Card className="space-y-4 p-5">
          {formal ? (
            <p className="text-sm text-foreground">
              <span className="text-muted-foreground">Aluno:</span> <strong>{student?.name}</strong>
              {student?.code ? ` · CGM ${student.code}` : ""} — o aluno não muda depois do registro.
            </p>
          ) : (
            <StudentPicker value={student} onChange={setStudent} error={errors.studentId} />
          )}
          <div className="grid gap-4 sm:grid-cols-3">
            <Input
              label="Data e hora do fato"
              name="occurredAt"
              type="datetime-local"
              defaultValue={toLocalInput(existing?.occurredAt ?? new Date().toISOString())}
              max={toLocalInput(new Date().toISOString())}
              error={errors.occurredAt}
              hint="A turma registrada é a da data do fato."
            />
            <Select label="Categoria" name="categoryId" defaultValue={existing?.categoryId} options={categories.map((c) => ({ value: c.id, label: c.name }))} />
            <Select
              label="Gravidade (opcional)"
              name="severityId"
              defaultValue={existing?.severityId ?? ""}
              options={[{ value: "", label: "Não informada" }, ...(options.data?.severities.map((s) => ({ value: s.id, label: s.name })) ?? [])]}
            />
          </div>
          {errors.categoryId ? <p className="text-xs text-critical-text">{errors.categoryId}</p> : null}
          <Input label="Local (opcional)" name="location" defaultValue={existing?.location ?? ""} maxLength={200} placeholder="Ex.: sala 12, pátio" />
          <Textarea label="Descrição do fato" name="description" rows={5} defaultValue={existing?.description ?? ""} error={errors.description} hint="Fatos observados, sem rótulos ou suposições sobre o aluno." />
          <Textarea label="Envolvidos (opcional)" name="involved" rows={2} defaultValue={existing?.involved ?? ""} hint="Só quando for legítimo registrar." />
          <Textarea label="Providências" name="actionsTaken" rows={3} defaultValue={existing?.actionsTaken ?? ""} />
          <Textarea label="Encaminhamentos" name="referrals" rows={2} defaultValue={existing?.referrals ?? ""} />
          <Textarea label="Observações" name="notes" rows={2} defaultValue={existing?.notes ?? ""} />
          {formal ? <Textarea label="Motivo da alteração" name="reason" rows={2} error={errors.reason} hint="Obrigatório: a alteração gera uma nova versão no histórico." /> : null}
        </Card>

        <div className="flex flex-wrap justify-end gap-2">
          {formal ? (
            <Button variant="primary" type="submit" value="save" disabled={save.isPending}>
              Salvar nova versão
            </Button>
          ) : (
            <>
              <Button type="submit" value="draft" disabled={save.isPending}>
                Salvar rascunho
              </Button>
              <Button variant="primary" type="submit" value="register" disabled={save.isPending}>
                Registrar ocorrência
              </Button>
            </>
          )}
        </div>
      </form>
    </>
  );
}

/** Busca por nome ou CGM. Mostra CGM e turma: alunos com nomes parecidos são escolhidos pela identidade, não pelo nome. */
function StudentPicker({
  value,
  onChange,
  error,
}: {
  value: { id: string; name: string; code: string | null; classroom: string | null } | null;
  onChange: (s: { id: string; name: string; code: string | null; classroom: string | null } | null) => void;
  error?: string;
}) {
  const [query, setQuery] = useState("");
  const results = useQuery({
    queryKey: ["student-picker", query],
    queryFn: () => schoolApi.students({ query, pageSize: 8 }),
    enabled: query.trim().length >= 2 && !value,
  });

  if (value) {
    return (
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border bg-surface-sunken px-4 py-3">
        <div>
          <p className="text-xs text-muted-foreground">Aluno</p>
          <p className="font-medium text-foreground">{value.name}</p>
          <p className="text-xs text-muted-foreground">
            {value.code ? `CGM ${value.code}` : "Sem CGM"} · {value.classroom ?? "Sem matrícula ativa"}
          </p>
        </div>
        <Button size="sm" variant="ghost" onClick={() => onChange(null)}>
          Trocar aluno
        </Button>
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <Input label="Aluno" placeholder="Nome ou CGM" value={query} onChange={(e) => setQuery(e.target.value)} error={error} autoComplete="off" />
      {results.data?.items.length ? (
        <ul className="divide-y divide-border rounded-xl border border-border">
          {results.data.items.map((s: StudentRow) => (
            <li key={s.id}>
              <button
                type="button"
                className="flex w-full items-center justify-between gap-3 px-4 py-2.5 text-left hover:bg-surface-sunken"
                onClick={() => onChange({ id: s.id, name: s.name, code: s.institutionalCode, classroom: s.classroom?.name ?? null })}
              >
                <span className="font-medium text-foreground">{s.name}</span>
                <span className="text-xs text-muted-foreground">
                  {s.institutionalCode ? `CGM ${s.institutionalCode}` : "Sem CGM"} · {s.classroom?.name ?? "—"}
                </span>
              </button>
            </li>
          ))}
        </ul>
      ) : query.trim().length >= 2 && results.isSuccess ? (
        <p className="flex items-center gap-1.5 text-sm text-muted-foreground">
          <Search className="size-4" aria-hidden /> Nenhum aluno encontrado.
        </p>
      ) : null}
    </div>
  );
}
