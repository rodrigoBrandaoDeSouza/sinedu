import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, CardBody, CardHeader, Dialog, Input, PageHeader, Select, Table, Td, Textarea, Th, Tr } from "@sinedu/ui";
import { KeyRound, LockOpen, Plus, Search, UserX } from "lucide-react";
import { useState, type FormEvent } from "react";
import { useIsSuperAdmin, useSession } from "../lib/session";

interface PlatformAccount {
  id: string;
  email: string;
  fullName: string;
  status: string;
  mfaEnabled: boolean;
  lockoutUntil: string | null;
  lastLoginAt: string | null;
  roles: string[];
}

interface LookupResult {
  id: string;
  email: string;
  fullName: string;
  kind: "Platform" | "School";
  status: string;
  mfaEnabled: boolean;
  locked: boolean;
  lastLoginAt: string | null;
  schools: string[];
}

const roleLabels: Record<string, string> = { SUPER_ADMIN: "Super admin", SUPORTE: "Suporte" };
const message = (err: unknown) => (err instanceof ApiError ? err.message : "Não foi possível concluir.");

/**
 * P-12: contas da plataforma e atendimento a contas de usuários (desbloqueio e reset de MFA).
 * Só SUPER_ADMIN. Toda ação avisa a pessoa por e-mail e fica na auditoria. Nada aqui dá acesso a dados de escola.
 */
export function AccountsPage() {
  const superAdmin = useIsSuperAdmin();
  if (!superAdmin) return <Alert tone="warning" title="Somente super admins gerenciam contas." />;
  return (
    <>
      <PageHeader title="Contas da plataforma" description="Equipe do painel e atendimento a contas (desbloqueio e verificação em duas etapas)." />
      <div className="space-y-4">
        <PlatformTeam />
        <AccountSupport />
      </div>
    </>
  );
}

function PlatformTeam() {
  const me = useSession();
  const client = useQueryClient();
  const [inviting, setInviting] = useState(false);
  const accounts = useQuery({ queryKey: ["platform-accounts"], queryFn: () => api.get<PlatformAccount[]>("/api/accounts") });
  const revoke = useMutation({
    mutationFn: (id: string) => api.post(`/api/accounts/${id}/revoke`),
    onSuccess: () => client.invalidateQueries({ queryKey: ["platform-accounts"] }),
  });

  return (
    <Card>
      <CardHeader
        title="Equipe da plataforma"
        description="Contas da plataforma são separadas das contas de escola e nunca têm papel em escolas."
        actions={
          <Button size="sm" variant="primary" onClick={() => setInviting(true)}>
            <Plus /> Convidar
          </Button>
        }
      />
      <div className="mt-4">
        {revoke.error ? <Alert tone="critical" className="mx-5 mb-3" title={message(revoke.error)} /> : null}
        <Table>
          <thead>
            <tr>
              <Th>Pessoa</Th>
              <Th>Papel</Th>
              <Th>Situação</Th>
              <Th>Último acesso</Th>
              <Th />
            </tr>
          </thead>
          <tbody>
            {accounts.data?.map((a) => (
              <Tr key={a.id}>
                <Td>
                  <p className="font-medium text-foreground">{a.fullName}</p>
                  <p className="text-xs text-muted-foreground">{a.email}</p>
                </Td>
                <Td>
                  {a.roles.length ? a.roles.map((r) => <Badge key={r} tone="brand" className="mr-1">{roleLabels[r] ?? r}</Badge>) : <Badge tone="neutral">Sem acesso</Badge>}
                </Td>
                <Td>
                  <Badge tone={a.status === "Active" ? "good" : a.status === "Invited" ? "info" : "neutral"} icon>
                    {a.status === "Active" ? "Ativa" : a.status === "Invited" ? "Convite enviado" : a.status}
                  </Badge>
                </Td>
                <Td className="text-foreground-secondary">{a.lastLoginAt ? new Date(a.lastLoginAt).toLocaleString("pt-BR") : "—"}</Td>
                <Td className="text-right">
                  {a.roles.length && a.email !== me.email ? (
                    <Button size="sm" variant="ghost" onClick={() => confirm(`Retirar o acesso de ${a.fullName} ao painel?`) && revoke.mutate(a.id)}>
                      <UserX /> Retirar acesso
                    </Button>
                  ) : null}
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      </div>
      {inviting ? <InviteDialog onClose={() => setInviting(false)} /> : null}
    </Card>
  );
}

function InviteDialog({ onClose }: { onClose: () => void }) {
  const client = useQueryClient();
  const invite = useMutation({
    mutationFn: (body: { email: string; fullName: string; role: string }) => api.post("/api/accounts", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["platform-accounts"] });
      onClose();
    },
  });
  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    invite.mutate({ email: String(d.get("email")).trim(), fullName: String(d.get("fullName")).trim(), role: String(d.get("role")) });
  };

  return (
    <Dialog
      open
      onClose={onClose}
      title="Convidar para o painel"
      description="A pessoa recebe um convite por e-mail, cria a senha e cadastra a verificação em duas etapas."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="invite-platform" disabled={invite.isPending}>
            Enviar convite
          </Button>
        </>
      }
    >
      <form id="invite-platform" onSubmit={submit} className="space-y-4" noValidate>
        {invite.error ? <Alert tone="critical" title={message(invite.error)} /> : null}
        <Input label="Nome completo" name="fullName" required />
        <Input label="E-mail" name="email" type="email" required hint="Use um e-mail que não tenha conta de escola (contas são separadas)." />
        <Select label="Papel" name="role" defaultValue="SUPORTE" options={[{ value: "SUPORTE", label: "Suporte" }, { value: "SUPER_ADMIN", label: "Super admin" }]} />
      </form>
    </Dialog>
  );
}

function AccountSupport() {
  const [email, setEmail] = useState("");
  const [resetting, setResetting] = useState(false);
  const lookup = useMutation({ mutationFn: (value: string) => api.get<LookupResult>(`/api/accounts/lookup?email=${encodeURIComponent(value)}`) });
  const unlock = useMutation({ mutationFn: (id: string) => api.post(`/api/accounts/users/${id}/unlock`), onSuccess: () => lookup.mutate(email) });
  const u = lookup.data;

  return (
    <Card>
      <CardHeader title="Atendimento a contas" description="Desbloqueio por tentativas erradas e reinício da verificação em duas etapas. A pessoa é avisada por e-mail." />
      <CardBody className="space-y-4">
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            if (email.trim()) lookup.mutate(email.trim());
          }}
        >
          <Input label="E-mail da conta" type="email" value={email} onChange={(e) => setEmail(e.target.value)} className="min-w-72 flex-1" />
          <Button type="submit" disabled={lookup.isPending}>
            <Search /> Buscar
          </Button>
        </form>
        {lookup.error ? <Alert tone="warning" title={lookup.error instanceof ApiError && lookup.error.status === 404 ? "Nenhuma conta com este e-mail." : message(lookup.error)} /> : null}
        {u ? (
          <div className="rounded-xl border border-border p-4">
            <div className="flex flex-wrap items-center gap-2">
              <p className="font-medium text-foreground">{u.fullName}</p>
              <Badge tone="neutral">{u.kind === "Platform" ? "Conta da plataforma" : "Conta de escola"}</Badge>
              <Badge tone={u.mfaEnabled ? "good" : "warning"} icon>
                {u.mfaEnabled ? "MFA ativo" : "MFA não cadastrado"}
              </Badge>
              {u.locked ? (
                <Badge tone="critical" icon>
                  Bloqueada
                </Badge>
              ) : null}
            </div>
            <p className="mt-1 text-xs text-muted-foreground">
              {u.email}
              {u.schools.length ? ` · ${u.schools.join(", ")}` : ""}
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Button size="sm" onClick={() => unlock.mutate(u.id)} disabled={!u.locked || unlock.isPending}>
                <LockOpen /> Desbloquear
              </Button>
              <Button size="sm" variant="danger" onClick={() => setResetting(true)} disabled={!u.mfaEnabled}>
                <KeyRound /> Reiniciar MFA
              </Button>
            </div>
            {unlock.isSuccess ? <Alert tone="good" className="mt-3" title="Conta desbloqueada. A pessoa foi avisada por e-mail." /> : null}
          </div>
        ) : null}
        {resetting && u ? <ResetMfaDialog account={u} onClose={() => setResetting(false)} onDone={() => lookup.mutate(email)} /> : null}
      </CardBody>
    </Card>
  );
}

function ResetMfaDialog({ account, onClose, onDone }: { account: LookupResult; onClose: () => void; onDone: () => void }) {
  const reset = useMutation({
    mutationFn: (reason: string) => api.post(`/api/accounts/users/${account.id}/reset-mfa`, { reason }),
    onSuccess: () => {
      onDone();
      onClose();
    },
  });

  return (
    <Dialog
      open
      onClose={onClose}
      title={`Reiniciar o MFA de ${account.fullName}?`}
      description="Só depois de confirmar a identidade da pessoa por outro canal. As sessões abertas caem e ela cadastra o autenticador de novo no próximo acesso."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="danger" type="submit" form="reset-mfa" disabled={reset.isPending}>
            Reiniciar MFA
          </Button>
        </>
      }
    >
      <form
        id="reset-mfa"
        noValidate
        onSubmit={(e) => {
          e.preventDefault();
          reset.mutate(String(new FormData(e.currentTarget).get("reason") ?? "").trim());
        }}
      >
        {reset.error ? <Alert tone="critical" className="mb-3" title={message(reset.error)} /> : null}
        <Textarea label="Motivo e como a identidade foi confirmada" name="reason" rows={3} />
      </form>
    </Dialog>
  );
}
