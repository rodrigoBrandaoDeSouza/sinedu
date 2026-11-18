import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, Dialog, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { ShieldCheck, UserPlus, X } from "lucide-react";
import { useState, type FormEvent } from "react";
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
                  <Td className="text-foreground-secondary">{m.lastSeenAt ? new Date(m.lastSeenAt).toLocaleString("pt-BR") : "Nunca"}</Td>
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
