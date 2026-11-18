import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, api, ApiError, Badge, Button, Card, Dialog, EmptyState, Input, PageHeader, Select, Table, Td, Th, Tr } from "@sinedu/ui";
import { Plus, UsersRound } from "lucide-react";
import { useState, type FormEvent } from "react";
import { schoolApi, staffFunctionLabels, type StaffFunction } from "../lib/school-api";

export function StaffPage() {
  const [adding, setAdding] = useState(false);
  const staff = useQuery({ queryKey: ["staff"], queryFn: schoolApi.staff });

  return (
    <>
      <PageHeader
        title="Equipe"
        description="Profissionais da escola. Professores cadastrados aqui podem ser vinculados às turmas. O acesso ao sistema é dado em Usuários e acessos."
        actions={
          <Button variant="primary" onClick={() => setAdding(true)}>
            <Plus />
            Adicionar profissional
          </Button>
        }
      />
      {staff.data?.length === 0 ? (
        <EmptyState icon={UsersRound} title="Nenhum profissional cadastrado" description="Cadastre direção, pedagogia e professores para aparecerem nas turmas e no painel do aluno." />
      ) : (
        <Card>
          <Table>
            <thead>
              <tr>
                <Th>Nome</Th>
                <Th>Função</Th>
                <Th>Contato profissional</Th>
                <Th>Acesso ao sistema</Th>
              </tr>
            </thead>
            <tbody>
              {staff.data?.map((s) => (
                <Tr key={s.id}>
                  <Td className="font-medium">{s.fullName}</Td>
                  <Td className="text-foreground-secondary">{staffFunctionLabels[s.function]}</Td>
                  <Td className="text-foreground-secondary">{[s.professionalEmail, s.professionalPhone].filter(Boolean).join(" · ") || "—"}</Td>
                  <Td>{s.hasAccount ? <Badge tone="good" icon>Com conta</Badge> : <Badge tone="neutral">Sem conta</Badge>}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}
      <AddStaffDialog open={adding} onClose={() => setAdding(false)} />
    </>
  );
}

function AddStaffDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [error, setError] = useState<string>();
  const add = useMutation({
    mutationFn: (body: Record<string, unknown>) => api.post("/api/school/staff", body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["staff"] });
      onClose();
    },
    onError: (err) => setError(err instanceof ApiError ? err.message : "Não foi possível adicionar."),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    setError(undefined);
    add.mutate({
      fullName: d.get("fullName"),
      function: d.get("function"),
      professionalEmail: d.get("email") || null,
      professionalPhone: d.get("phone") || null,
    });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Adicionar profissional"
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="staff-form" disabled={add.isPending}>
            Adicionar
          </Button>
        </>
      }
    >
      <form id="staff-form" onSubmit={submit} className="space-y-4">
        {error ? <Alert tone="critical" title={error} /> : null}
        <Input label="Nome completo" name="fullName" required />
        <Select
          label="Função"
          name="function"
          defaultValue="Professor"
          options={(Object.keys(staffFunctionLabels) as StaffFunction[]).map((f) => ({ value: f, label: staffFunctionLabels[f] }))}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="E-mail profissional" name="email" type="email" />
          <Input label="Telefone profissional" name="phone" />
        </div>
      </form>
    </Dialog>
  );
}
