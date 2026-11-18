import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, Input } from "@sinedu/ui";
import { ArrowLeft, Cake, Hash, Home, Lock, Mail, Pencil, Phone, ShieldCheck, UserRound } from "lucide-react";
import { lazy, Suspense, useState, type FormEvent, type ReactNode } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { useHasRole } from "../lib/session";

/** Só baixado quando Direção/Pedagogia abre Saúde ou Proteção: os demais perfis nunca carregam este código. */
const ProtectedPanel = lazy(() => import("./protected/protected-panel"));

type Tab = "cadastro" | "saude" | "protecao";
import { enrollmentLabels, formatDate, initials, relationshipLabels, schoolApi, type StudentDetails } from "../lib/school-api";

/** Painel do aluno. Abas de Saúde e Proteção/Sigilo existem só para Direção e Pedagogia. */
export function StudentPage() {
  const { id = "" } = useParams();
  const [editing, setEditing] = useState(false);
  const pedagogical = useHasRole("DIRETOR", "PEDAGOGO");
  const [params, setParams] = useSearchParams();
  const requested = params.get("aba");
  const tab: Tab = pedagogical && (requested === "saude" || requested === "protecao") ? requested : "cadastro";
  const { data: s, error } = useQuery({ queryKey: ["student", id], queryFn: () => schoolApi.student(id) });

  if (error instanceof ApiError && error.status === 404) {
    return <Alert tone="warning" title="Aluno não encontrado ou fora do seu acesso." />;
  }

  if (!s) {
    return <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" aria-busy="true" />;
  }

  return (
    <>
      <div className="mb-4 flex items-center justify-between gap-3">
        <Link to="/alunos" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="size-4" aria-hidden /> Alunos
        </Link>
        {s.canEdit ? (
          <Button onClick={() => setEditing(true)}>
            <Pencil />
            Editar cadastro
          </Button>
        ) : null}
      </div>

      <Card className="mb-4 p-5">
        <div className="flex flex-wrap items-center gap-4">
          <span className="grid size-14 shrink-0 place-items-center rounded-2xl bg-primary-soft text-lg font-semibold text-primary-soft-foreground">
            {initials(s.socialName ?? s.fullName)}
          </span>
          <div className="min-w-0 flex-1">
            <h1 className="text-xl font-semibold tracking-tight text-foreground sm:text-2xl">{s.socialName ?? s.fullName}</h1>
            {s.socialName ? <p className="text-sm text-muted-foreground">Nome civil: {s.fullName}</p> : null}
            <div className="mt-2 flex flex-wrap gap-2">
              {s.classroom ? (
                <Link to={`/turmas/${s.classroom.id}`}>
                  <Badge tone="brand">{s.classroom.name}</Badge>
                </Link>
              ) : (
                <Badge tone="neutral">Sem matrícula ativa</Badge>
              )}
              {s.status === "Inactive" ? <Badge tone="neutral">Inativo</Badge> : null}
            </div>
          </div>
        </div>
      </Card>

      {pedagogical ? (
        <div role="tablist" aria-label="Seções do aluno" className="mb-4 flex gap-1 border-b border-border">
          {(
            [
              ["cadastro", "Cadastro"],
              ["saude", "Saúde"],
              ["protecao", "Proteção/Sigilo"],
            ] as const
          ).map(([value, label]) => (
            <button
              key={value}
              role="tab"
              type="button"
              aria-selected={tab === value}
              onClick={() => setParams(value === "cadastro" ? {} : { aba: value }, { replace: true })}
              className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
                tab === value ? "border-primary text-foreground" : "border-transparent text-muted-foreground hover:text-foreground"
              }`}
            >
              {value !== "cadastro" ? <Lock className="mr-1 inline size-3.5" aria-hidden /> : null}
              {label}
            </button>
          ))}
        </div>
      ) : null}

      {tab !== "cadastro" ? (
        <Suspense fallback={<div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" aria-busy="true" />}>
          <ProtectedPanel studentId={s.id} tab={tab} />
        </Suspense>
      ) : (
      <div className="grid gap-4 xl:grid-cols-3">
        <div className="space-y-4 xl:col-span-2">
          <Card>
            <CardHeader title="Dados cadastrais" />
            <CardBody>
              <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
                <Field icon={Hash} label="CGM" value={s.institutionalCode} mono />
                <Field icon={Cake} label="Nascimento" value={s.birthDate ? `${formatDate(s.birthDate)}${s.age != null ? ` · ${s.age} anos` : ""}` : null} />
                <Field icon={Mail} label="E-mail institucional" value={s.institutionalEmail} />
                {s.address !== undefined ? <Field icon={Home} label="Endereço" value={s.address} /> : null}
              </dl>
            </CardBody>
          </Card>

          {s.guardians !== undefined ? <GuardiansCard guardians={s.guardians} /> : null}
        </div>

        <Card>
          <CardHeader title="Histórico de matrículas" description="Mudanças de turma ficam registradas." />
          <CardBody>
            <ol className="relative space-y-4 border-l border-border pl-5">
              {s.enrollments.map((e) => (
                <li key={e.id} className="relative">
                  <span
                    aria-hidden
                    className={`absolute -left-[25px] top-1.5 size-2.5 rounded-full ring-4 ring-surface ${e.status === "Ativa" ? "bg-primary" : "bg-border-strong"}`}
                  />
                  <p className="text-sm font-medium text-foreground">
                    {e.classroom} {e.year ? <span className="font-normal text-muted-foreground">· {e.year}</span> : null}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {enrollmentLabels[e.status]} · desde {formatDate(e.startsOn)}
                    {e.endsOn ? ` até ${formatDate(e.endsOn)}` : ""}
                    {e.callNumber ? ` · nº ${e.callNumber}` : ""}
                  </p>
                </li>
              ))}
            </ol>
          </CardBody>
        </Card>
      </div>

      )}

      {s.canEdit ? <EditStudentDialog student={s} open={editing} onClose={() => setEditing(false)} /> : null}
    </>
  );
}

function GuardiansCard({ guardians }: { guardians: NonNullable<StudentDetails["guardians"]> }) {
  return (
    <Card>
      <CardHeader title="Responsáveis" description="Comunicações só para quem tem autorização de contato registrada." />
      <CardBody className="space-y-3">
        {guardians.length === 0 ? <p className="text-sm text-muted-foreground">Nenhum responsável cadastrado.</p> : null}
        {guardians.map((g) => (
          <div key={g.id} className="rounded-xl border border-border p-4">
            <div className="flex flex-wrap items-center gap-2">
              <UserRound className="size-4 text-muted-foreground" aria-hidden />
              <p className="font-medium text-foreground">{g.name}</p>
              <Badge tone="neutral">{relationshipLabels[g.relationship]}</Badge>
              {g.isPrimaryContact ? <Badge tone="info">Contato principal</Badge> : null}
              <Badge tone={g.contactAuthorized ? "good" : "warning"} icon>
                {g.contactAuthorized ? "Contato autorizado" : "Autorização não registrada"}
              </Badge>
            </div>
            <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-3">
              <Field icon={Phone} label="Telefone" value={g.phone} />
              <Field icon={Mail} label="E-mail" value={g.email} />
              {g.document !== undefined ? <Field icon={ShieldCheck} label="Documento" value={g.document} mono /> : null}
            </dl>
          </div>
        ))}
      </CardBody>
    </Card>
  );
}

function Field({ icon: Icon, label, value, mono }: { icon: typeof Hash; label: string; value: ReactNode | null | undefined; mono?: boolean }) {
  return (
    <div className="flex gap-2.5">
      <Icon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
      <div className="min-w-0">
        <dt className="text-xs text-muted-foreground">{label}</dt>
        <dd className={`mt-0.5 text-sm text-foreground ${mono ? "font-mono" : ""} ${value ? "" : "text-muted-foreground"}`}>{value || "Não informado"}</dd>
      </div>
    </div>
  );
}

function EditStudentDialog({ student, open, onClose }: { student: StudentDetails; open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const save = useMutation({
    mutationFn: (body: Record<string, unknown>) => api.put(`/api/students/${student.id}`, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["student", student.id] });
      client.invalidateQueries({ queryKey: ["students"] });
      onClose();
    },
    onError: (err) =>
      setErrors(
        err instanceof ApiError && Object.keys(err.fieldErrors).length
          ? Object.fromEntries(Object.entries(err.fieldErrors).map(([k, v]) => [k, v[0] ?? ""]))
          : { form: err instanceof ApiError ? err.message : "Não foi possível salvar." },
      ),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const text = (k: string) => (String(d.get(k) ?? "").trim() || null);
    setErrors({});
    save.mutate({
      fullName: text("fullName") ?? "",
      socialName: text("socialName"),
      birthDate: text("birthDate"),
      institutionalCode: text("institutionalCode"),
      institutionalEmail: text("institutionalEmail"),
      address: text("address"),
    });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Editar cadastro"
      description="Alterações ficam registradas na auditoria. O endereço anterior é guardado no histórico."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="student-form" disabled={save.isPending}>
            Salvar
          </Button>
        </>
      }
    >
      <form id="student-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Nome completo (civil)" name="fullName" defaultValue={student.fullName} error={errors.fullName} required />
        <Input label="Nome social (opcional)" name="socialName" defaultValue={student.socialName ?? ""} hint="Quando informado, é o nome exibido em todo o sistema." />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Nascimento" name="birthDate" type="date" defaultValue={student.birthDate ?? ""} />
          <Input label="CGM" name="institutionalCode" inputMode="numeric" defaultValue={student.institutionalCode ?? ""} error={errors.institutionalCode} />
        </div>
        <Input label="E-mail institucional" name="institutionalEmail" type="email" defaultValue={student.institutionalEmail ?? ""} />
        {student.address !== undefined ? <Input label="Endereço" name="address" defaultValue={student.address ?? ""} /> : null}
      </form>
    </Dialog>
  );
}
