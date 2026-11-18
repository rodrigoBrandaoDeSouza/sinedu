import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, Dialog, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { BookOpenCheck, ShieldCheck, UserPlus, X } from "lucide-react";
import { useState, type FormEvent } from "react";
import { schoolApi } from "../lib/school-api";
import { roleLabels, useSession, type SchoolRole } from "../lib/session";

interface MemberRow {
  userId: string;
  displayName: string;
  email: string;
  lastSeenAt: string | null;
  roles: { role: SchoolRole; grantedAt: string; expiresAt: string | null }[];
}

/** Espelha a regra do back-end (o back-end é quem decide; isto só evita oferecer opções inúteis). */
function grantableRoles(actor: SchoolRole[]): SchoolRole[] {
  if (actor.includes("DIRETOR")) return ["DIRETOR", "PEDAGOGO", "ADMIN_TECNICO", "PROFESSOR", "CONSULTA"];
  if (actor.includes("ADMIN_TECNICO")) return ["PROFESSOR", "CONSULTA"];
  return [];
}

const sensitiveRoles: SchoolRole[] = ["DIRETOR", "PEDAGOGO"];

export function UsersPage() {
  const me = useSession();
  const client = useQueryClient();
  const [inviting, setInviting] = useState(false);
  const [granting, setGranting] = useState<MemberRow | null>(null);
  const { data: members, isLoading } = useQuery({ queryKey: ["members"], queryFn: () => api.get<MemberRow[]>("/api/access/members") });
  const grantable = grantableRoles(me.roles);

  const revoke = useMutation({
    mutationFn: ({ userId, role }: { userId: string; role: SchoolRole }) => api.delete(`/api/access/members/${userId}/roles/${role}`),
    onSuccess: () => client.invalidateQueries({ queryKey: ["members"] }),
  });

  return (
    <>
      <PageHeader
        title="Usuários e acessos"
        description="Quem acessa esta escola e com qual papel. Cada pessoa entra com a própria conta."
        actions={
          grantable.length ? (
            <Button variant="primary" onClick={() => setInviting(true)}>
              <UserPlus />
              Convidar pessoa
            </Button>
          ) : null
        }
      />

      {revoke.error ? (
        <Alert tone="critical" title={revoke.error instanceof ApiError ? revoke.error.message : "Não foi possível revogar."} className="mb-4" />
      ) : null}

      <Card>
        <Table>
          <thead>
            <tr>
              <Th>Pessoa</Th>
              <Th>Papéis</Th>
              <Th>Último acesso</Th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <Tr>
                <Td colSpan={3} className="text-muted-foreground">
                  Carregando…
                </Td>
              </Tr>
            ) : (
              members?.map((m) => (
                <Tr key={m.userId}>
                  <Td>
                    <p className="font-medium">{m.displayName}</p>
                    <p className="text-xs text-muted-foreground">{m.email}</p>
                  </Td>
                  <Td>
                    <div className="flex flex-wrap gap-1.5">
                      {m.roles.map((r) => (
                        <span key={r.role} className="inline-flex items-center">
                          <Badge tone={sensitiveRoles.includes(r.role) ? "brand" : "neutral"}>
                            {sensitiveRoles.includes(r.role) ? <ShieldCheck aria-hidden /> : null}
                            {roleLabels[r.role]}
                            {grantable.includes(r.role) && m.userId !== me.user.id ? (
                              <button
                                type="button"
                                className="-mr-1 ml-0.5 rounded-full p-0.5 hover:bg-black/10"
                                aria-label={`Revogar ${roleLabels[r.role]} de ${m.displayName}`}
                                onClick={() => {
                                  if (confirm(`Revogar o papel ${roleLabels[r.role]} de ${m.displayName}?`)) revoke.mutate({ userId: m.userId, role: r.role });
                                }}
                              >
                                <X className="size-3" />
                              </button>
                            ) : null}
                          </Badge>
                        </span>
                      ))}
                    </div>
                  </Td>
                  <Td className="text-foreground-secondary">
                    <div className="flex items-center justify-between gap-2">
                      {m.lastSeenAt ? new Date(m.lastSeenAt).toLocaleString("pt-BR") : "Nunca"}
                      {grantable.includes("CONSULTA") && m.roles.some((r) => r.role === "CONSULTA") ? (
                        <Button size="sm" variant="ghost" onClick={() => setGranting(m)}>
                          <BookOpenCheck />
                          Turmas
                        </Button>
                      ) : null}
                    </div>
                  </Td>
                </Tr>
              ))
            )}
          </tbody>
        </Table>
      </Card>

      <p className="mt-4 flex items-center gap-1.5 text-xs text-muted-foreground">
        <ShieldCheck className="size-3.5" aria-hidden />
        Direção e Pedagogia acessam dados de Saúde e Proteção. Toda concessão desses papéis é avisada por e-mail a todos os diretores.
      </p>

      <InviteDialog open={inviting} roles={grantable} onClose={() => setInviting(false)} />
      {granting ? <ReadOnlyGrantsDialog member={granting} onClose={() => setGranting(null)} /> : null}
    </>
  );
}

function InviteDialog({ open, roles, onClose }: { open: boolean; roles: SchoolRole[]; onClose: () => void }) {
  const client = useQueryClient();
  const [error, setError] = useState<string>();
  const invite = useMutation({
    mutationFn: (body: { email: string; fullName: string; role: SchoolRole }) => api.post("/api/access/invitations", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["members"] });
      onClose();
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : "Não foi possível enviar o convite."),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    setError(undefined);
    invite.mutate({ email: String(data.get("email")), fullName: String(data.get("fullName")), role: data.get("role") as SchoolRole });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Convidar pessoa"
      description="A pessoa recebe um e-mail para criar a senha. Se já tiver conta no Sinedu, só ganha acesso a esta escola."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="invite-form" disabled={invite.isPending}>
            {invite.isPending ? "Enviando…" : "Enviar convite"}
          </Button>
        </>
      }
    >
      <form id="invite-form" onSubmit={submit} className="space-y-4" noValidate>
        {error ? <Alert tone="critical" title={error} /> : null}
        <Input label="Nome completo" name="fullName" required autoComplete="off" />
        <Input label="E-mail" name="email" type="email" required autoComplete="off" />
        <Select label="Papel" name="role" options={roles.map((r) => ({ value: r, label: roleLabels[r] }))} defaultValue={roles.at(-1)} />
      </form>
    </Dialog>
  );
}

/** D10: o perfil Consulta só enxerga as turmas concedidas aqui. */
function ReadOnlyGrantsDialog({ member, onClose }: { member: MemberRow; onClose: () => void }) {
  const [selected, setSelected] = useState<Set<string> | null>(null);
  const classrooms = useQuery({ queryKey: ["classrooms"], queryFn: () => schoolApi.classrooms() });
  const current = useQuery({ queryKey: ["grants", member.userId], queryFn: () => api.get<string[]>(`/api/school/readonly-grants/${member.userId}`) });
  const save = useMutation({ mutationFn: (ids: string[]) => api.put(`/api/school/readonly-grants/${member.userId}`, ids), onSuccess: onClose });
  const chosen = selected ?? new Set(current.data ?? []);

  const toggle = (id: string) => {
    const next = new Set(chosen);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    setSelected(next);
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title={`Turmas de ${member.displayName}`}
      description="O perfil Consulta vê apenas dados cadastrais dos alunos destas turmas."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" disabled={save.isPending || !current.data} onClick={() => save.mutate([...chosen])}>
            Salvar ({chosen.size})
          </Button>
        </>
      }
    >
      {save.error ? <Alert tone="critical" className="mb-3" title={save.error instanceof ApiError ? save.error.message : "Não foi possível salvar."} /> : null}
      <div className="grid max-h-80 grid-cols-2 gap-2 overflow-y-auto sm:grid-cols-3">
        {classrooms.data?.map((c) => (
          <label key={c.id} className="flex cursor-pointer items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm hover:bg-surface-sunken">
            <input type="checkbox" className="size-4 accent-(--primary)" checked={chosen.has(c.id)} onChange={() => toggle(c.id)} />
            {c.name}
          </label>
        ))}
      </div>
    </Dialog>
  );
}
