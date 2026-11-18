import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardHeader, Dialog, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { ArrowLeft, UserPlus, X } from "lucide-react";
import { useState, type FormEvent } from "react";
import { Link, useNavigate, useParams } from "react-router";
import { useHasRole } from "../lib/session";
import { schoolApi, shiftLabels } from "../lib/school-api";

export function ClassroomPage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const client = useQueryClient();
  const canManageTeachers = useHasRole("DIRETOR", "PEDAGOGO", "ADMIN_TECNICO");
  const seesStudents = useHasRole("DIRETOR", "PEDAGOGO", "PROFESSOR", "CONSULTA", "SUPORTE_PLATAFORMA");
  const [assigning, setAssigning] = useState(false);

  const classroom = useQuery({ queryKey: ["classroom", id], queryFn: () => schoolApi.classroom(id) });
  const students = useQuery({
    queryKey: ["students", { classroomId: id }],
    queryFn: () => schoolApi.students({ classroomId: id, pageSize: 100 }),
    enabled: seesStudents,
  });
  const unassign = useMutation({
    mutationFn: (assignmentId: string) => api.delete(`/api/school/classrooms/${id}/teachers/${assignmentId}`),
    onSuccess: () => client.invalidateQueries({ queryKey: ["classroom", id] }),
  });

  if (classroom.error instanceof ApiError && classroom.error.status === 404) {
    return <Alert tone="warning" title="Turma não encontrada ou fora do seu acesso." />;
  }

  const c = classroom.data;
  const rows = [...(students.data?.items ?? [])].sort((a, b) => (a.callNumber ?? 999) - (b.callNumber ?? 999));

  return (
    <>
      <PageHeader
        eyebrow={
          <Link to="/turmas" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="size-4" aria-hidden /> Turmas
          </Link>
        }
        title={c ? c.name : "Turma"}
        description={c ? `${c.gradeName} · turno ${shiftLabels[c.shift].toLowerCase()}${c.externalCode ? ` · código ${c.externalCode}` : ""}` : undefined}
      />

      <div className="grid gap-4 xl:grid-cols-3">
        {seesStudents ? (
          <Card className="xl:col-span-2">
            <CardHeader title="Alunos" description={students.data ? `${students.data.total} com matrícula ativa` : "Carregando…"} />
            <div className="mt-4">
              <Table>
                <thead>
                  <tr>
                    <Th align="right">Nº</Th>
                    <Th>Nome</Th>
                    <Th>CGM</Th>
                    <Th align="right">Idade</Th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((s) => (
                    <Tr key={s.id} className="cursor-pointer" onClick={() => navigate(`/alunos/${s.id}`)}>
                      <Td align="right" className="w-14 text-muted-foreground">{s.callNumber ?? "—"}</Td>
                      <Td>
                        <Link to={`/alunos/${s.id}`} className="font-medium hover:text-primary" onClick={(e) => e.stopPropagation()}>
                          {s.name}
                        </Link>
                        {s.civilName ? <span className="ml-2 text-xs text-muted-foreground">(nome social)</span> : null}
                      </Td>
                      <Td className="font-mono text-xs text-foreground-secondary">{s.institutionalCode ?? "—"}</Td>
                      <Td align="right">{s.age ?? "—"}</Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </div>
          </Card>
        ) : null}

        <Card className={seesStudents ? "" : "xl:col-span-3"}>
          <CardHeader
            title="Professores"
            description="Quem leciona nesta turma vê os alunos dela."
            actions={
              canManageTeachers ? (
                <Button size="sm" onClick={() => setAssigning(true)}>
                  <UserPlus />
                  Vincular
                </Button>
              ) : null
            }
          />
          <ul className="space-y-2 px-5 pb-5 pt-4">
            {c?.teachers.length ? (
              c.teachers.map((t) => (
                <li key={t.assignmentId} className="flex items-center gap-3 rounded-lg border border-border px-3 py-2">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-foreground">{t.name}</p>
                    {t.subject ? <p className="text-xs text-muted-foreground">{t.subject}</p> : null}
                  </div>
                  {canManageTeachers ? (
                    <Button variant="ghost" size="icon-sm" aria-label={`Desvincular ${t.name}`} onClick={() => unassign.mutate(t.assignmentId)}>
                      <X />
                    </Button>
                  ) : null}
                </li>
              ))
            ) : (
              <li className="text-sm text-muted-foreground">Nenhum professor vinculado.</li>
            )}
          </ul>
          {c?.status === "Closed" ? (
            <div className="px-5 pb-5">
              <Badge tone="neutral">Turma encerrada</Badge>
            </div>
          ) : null}
        </Card>
      </div>

      {canManageTeachers ? <AssignTeacherDialog classroomId={id} open={assigning} onClose={() => setAssigning(false)} /> : null}
    </>
  );
}

function AssignTeacherDialog({ classroomId, open, onClose }: { classroomId: string; open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [error, setError] = useState<string>();
  const staff = useQuery({ queryKey: ["staff"], queryFn: schoolApi.staff, enabled: open });
  const assign = useMutation({
    mutationFn: (body: { staffMemberId: string; subject: string }) => api.post(`/api/school/classrooms/${classroomId}/teachers`, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["classroom", classroomId] });
      onClose();
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : "Não foi possível vincular."),
  });
  const teachers = (staff.data ?? []).filter((s) => s.function === "Professor");

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    assign.mutate({ staffMemberId: String(data.get("staffMemberId")), subject: String(data.get("subject") ?? "") });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Vincular professor"
      description="Cadastre o profissional em Equipe antes, se ele ainda não aparecer na lista."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="assign-form" disabled={!teachers.length || assign.isPending}>
            Vincular
          </Button>
        </>
      }
    >
      <form id="assign-form" onSubmit={submit} className="space-y-4">
        {error ? <Alert tone="critical" title={error} /> : null}
        {teachers.length ? (
          <Select label="Professor(a)" name="staffMemberId" options={teachers.map((t) => ({ value: t.id, label: t.fullName }))} />
        ) : (
          <Alert tone="info" title="Nenhum professor cadastrado em Equipe." />
        )}
        <Input label="Disciplina (opcional)" name="subject" placeholder="Ex.: Matemática" />
      </form>
    </Dialog>
  );
}
