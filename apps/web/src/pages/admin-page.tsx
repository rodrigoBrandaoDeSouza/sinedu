import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, Input, PageHeader } from "@sinedu/ui";
import { CalendarRange, CheckCircle2, Plus, Save } from "lucide-react";
import { useState, type FormEvent } from "react";
import { formatDate, schoolApi, type SchoolYear } from "../lib/school-api";

interface Profile {
  name: string;
  institutionalCode: string | null;
  address: string | null;
  phone: string | null;
  email: string | null;
}

export function AdminPage() {
  return (
    <>
      <PageHeader title="Administração" description="Dados da escola e calendário letivo. Categorias de ocorrência e modelos de e-mail chegam com a Fase 5." />
      <div className="grid gap-4 xl:grid-cols-5">
        <div className="xl:col-span-3">
          <YearsCard />
        </div>
        <div className="xl:col-span-2">
          <ProfileCard />
        </div>
      </div>
    </>
  );
}

function YearsCard() {
  const client = useQueryClient();
  const [creating, setCreating] = useState(false);
  const years = useQuery({ queryKey: ["years"], queryFn: schoolApi.years });
  const activate = useMutation({
    mutationFn: (id: string) => api.post(`/api/school/years/${id}/activate`),
    onSuccess: () => client.invalidateQueries(),
  });

  return (
    <Card>
      <CardHeader
        title="Anos letivos e bimestres"
        description="O ano ativo define as turmas e os indicadores exibidos."
        actions={
          <Button size="sm" onClick={() => setCreating(true)}>
            <Plus />
            Novo ano letivo
          </Button>
        }
      />
      <CardBody className="space-y-4">
        {years.data?.length === 0 ? <Alert tone="info" title="Nenhum ano letivo cadastrado." /> : null}
        {years.data?.map((y) => (
          <YearBlock key={y.id} year={y} onActivate={() => activate.mutate(y.id)} />
        ))}
      </CardBody>
      <CreateYearDialog open={creating} onClose={() => setCreating(false)} />
    </Card>
  );
}

function YearBlock({ year: y, onActivate }: { year: SchoolYear; onActivate: () => void }) {
  return (
    <div className="rounded-xl border border-border p-4">
      <div className="flex flex-wrap items-center gap-2">
        <CalendarRange className="size-4 text-muted-foreground" aria-hidden />
        <p className="text-base font-semibold text-foreground">{y.year}</p>
        <span className="text-sm text-muted-foreground">
          {formatDate(y.startsOn)} a {formatDate(y.endsOn)}
        </span>
        {y.status === "Active" ? (
          <Badge tone="good" icon>
            Ativo
          </Badge>
        ) : (
          <>
            <Badge tone="neutral">{y.status === "Planned" ? "Planejado" : "Encerrado"}</Badge>
            <Button size="sm" variant="ghost" className="ml-auto" onClick={onActivate}>
              <CheckCircle2 />
              Tornar ativo
            </Button>
          </>
        )}
      </div>
      {y.terms.length ? (
        <ol className="mt-3 grid gap-2 sm:grid-cols-2 xl:grid-cols-4">
          {y.terms.map((t) => (
            <li key={t.id} className="rounded-lg bg-surface-sunken px-3 py-2">
              <p className="text-sm font-medium text-foreground">{t.name}</p>
              <p className="text-xs text-muted-foreground tabular-nums">
                {formatDate(t.startsOn).slice(0, 5)} – {formatDate(t.endsOn).slice(0, 5)}
              </p>
            </li>
          ))}
        </ol>
      ) : (
        <p className="mt-2 text-sm text-muted-foreground">Sem bimestres cadastrados.</p>
      )}
    </div>
  );
}

function CreateYearDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [error, setError] = useState<string>();
  const nextYear = new Date().getFullYear() + 1;
  const create = useMutation({
    mutationFn: (body: Record<string, unknown>) => api.post("/api/school/years", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["years"] });
      onClose();
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : "Não foi possível criar."),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    setError(undefined);
    create.mutate({
      year: Number(d.get("year")),
      startsOn: d.get("startsOn"),
      endsOn: d.get("endsOn"),
      status: d.get("active") ? "Active" : "Planned",
      createStandardTerms: true,
    });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Novo ano letivo"
      description="Criamos 4 bimestres de duração parecida; ajuste as datas depois se precisar."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="year-form" disabled={create.isPending}>
            Criar
          </Button>
        </>
      }
    >
      <form id="year-form" onSubmit={submit} className="space-y-4">
        {error ? <Alert tone="critical" title={error} /> : null}
        <Input label="Ano" name="year" type="number" defaultValue={nextYear} required />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Início" name="startsOn" type="date" defaultValue={`${nextYear}-02-02`} required />
          <Input label="Fim" name="endsOn" type="date" defaultValue={`${nextYear}-12-18`} required />
        </div>
        <label className="flex items-center gap-2 text-sm text-foreground">
          <input type="checkbox" name="active" className="size-4 accent-(--primary)" />
          Tornar este o ano ativo (encerra o atual)
        </label>
      </form>
    </Dialog>
  );
}

function ProfileCard() {
  const client = useQueryClient();
  const [saved, setSaved] = useState(false);
  const profile = useQuery({ queryKey: ["profile"], queryFn: () => api.get<Profile>("/api/school/profile") });
  const save = useMutation({
    mutationFn: (body: Profile) => api.put("/api/school/profile", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["profile"] });
      setSaved(true);
    },
  });

  if (!profile.data) return <Card className="h-80 animate-pulse" />;

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const text = (k: string) => (String(d.get(k) ?? "").trim() || null);
    setSaved(false);
    save.mutate({ name: text("name") ?? "", institutionalCode: text("institutionalCode"), address: text("address"), phone: text("phone"), email: text("email") });
  };

  return (
    <Card>
      <CardHeader title="Dados da escola" description="Aparecem no cabeçalho das atas e comunicações." />
      <CardBody>
        <form onSubmit={submit} className="space-y-4">
          {saved ? <Alert tone="good" title="Dados salvos." /> : null}
          {save.error ? <Alert tone="critical" title={save.error instanceof ApiError ? save.error.message : "Não foi possível salvar."} /> : null}
          <Input label="Nome" name="name" defaultValue={profile.data.name} required />
          <Input label="Código INEP" name="institutionalCode" defaultValue={profile.data.institutionalCode ?? ""} inputMode="numeric" />
          <Input label="Endereço" name="address" defaultValue={profile.data.address ?? ""} />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Telefone" name="phone" defaultValue={profile.data.phone ?? ""} />
            <Input label="E-mail" name="email" type="email" defaultValue={profile.data.email ?? ""} />
          </div>
          <Button type="submit" variant="primary" disabled={save.isPending}>
            <Save />
            Salvar
          </Button>
        </form>
      </CardBody>
    </Card>
  );
}
