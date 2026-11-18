import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Dialog, EmptyState, Input, Textarea } from "@sinedu/ui";
import { HeartPulse, Pencil, Plus, Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { formatDate } from "../../lib/school-api";
import { isStepUpRequired, protectedApi, type EmergencyContact, type HealthData, type HealthView } from "../../lib/protected-api";
import { Attachments, ProtectedCard, ProtectedState, Section, StepUpDialog, UpdatedBy, VersionHistory } from "./shared";
import { errorsOf } from "./utils";

export function HealthTab({ studentId }: { studentId: string }) {
  const key = ["protected", studentId, "health"];
  const health = useQuery({ queryKey: key, queryFn: () => protectedApi.health(studentId), retry: false });
  const [editing, setEditing] = useState(false);
  const record = health.data?.record ?? null;

  return (
    <ProtectedState error={health.error}>
      {health.isPending ? (
        <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" aria-busy="true" />
      ) : record === null ? (
        <EmptyState
          icon={HeartPulse}
          title="Nenhuma ficha de saúde"
          description="Registre só o necessário para o atendimento na escola, com a origem da informação."
          action={
            <Button variant="primary" onClick={() => setEditing(true)}>
              <Plus />
              Criar ficha
            </Button>
          }
        />
      ) : (
        <ProtectedCard
          title="Ficha de saúde"
          description="Informações fornecidas pela família ou por documento. Sem diagnóstico, risco ou recomendação automática."
          actions={
            <div className="flex flex-wrap items-center gap-2">
              <UpdatedBy name={record.updatedByName} at={record.updatedAt} version={record.version} />
              <VersionHistory
                queryKey={key}
                list={() => protectedApi.healthVersions(studentId)}
                load={(v) => protectedApi.healthVersion(studentId, v)}
                render={(d) => <HealthDetails data={d} />}
              />
              <Button size="sm" onClick={() => setEditing(true)}>
                <Pencil />
                Editar
              </Button>
            </div>
          }
        >
          {record.reviewDueOn ? (
            <Badge tone={record.reviewDueOn <= new Date().toISOString().slice(0, 10) ? "warning" : "info"} icon>
              Revisão prevista para {formatDate(record.reviewDueOn)}
            </Badge>
          ) : null}
          <HealthDetails data={record.data} />
          <Attachments studentId={studentId} owner="Health" ownerId={record.id} />
        </ProtectedCard>
      )}
      <HealthDialog key={record?.version ?? 0} studentId={studentId} record={record} open={editing} onClose={() => setEditing(false)} />
    </ProtectedState>
  );
}

export function HealthDetails({ data }: { data: HealthData }) {
  return (
    <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
      <Section label="Tipo sanguíneo" value={data.bloodType} />
      <Section label="Origem da informação" value={data.source} />
      <Section label="Alergias e cuidados" value={data.allergies} />
      <Section label="Condições relevantes para a escola" value={data.conditions} />
      <Section label="Medicamentos informados" value={data.medications} />
      <Section label="Restrições e adaptações" value={data.restrictions} />
      <Section label="Primeiros cuidados autorizados" value={data.firstAidInstructions} />
      <Section
        label="Contatos de emergência"
        value={
          data.emergencyContacts?.length
            ? data.emergencyContacts.map((c) => `${c.name}${c.relationship ? ` (${c.relationship})` : ""}${c.phone ? ` · ${c.phone}` : ""}`).join("\n")
            : null
        }
      />
      <Section label="Observações" value={data.notes} />
    </dl>
  );
}

function HealthDialog({ studentId, record, open, onClose }: { studentId: string; record: HealthView | null; open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [stepUp, setStepUp] = useState(false);
  const [contacts, setContacts] = useState<EmergencyContact[]>(record?.data.emergencyContacts ?? []);
  const save = useMutation({
    mutationFn: (body: Parameters<typeof protectedApi.saveHealth>[1]) => protectedApi.saveHealth(studentId, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["protected", studentId, "health"] });
      onClose();
    },
    onError: (err) => (isStepUpRequired(err) ? setStepUp(true) : setErrors(errorsOf(err))),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const d = new FormData(e.currentTarget);
    const text = (k: string) => String(d.get(k) ?? "").trim() || null;
    setErrors({});
    save.mutate({
      data: {
        bloodType: text("bloodType"),
        allergies: text("allergies"),
        conditions: text("conditions"),
        medications: text("medications"),
        restrictions: text("restrictions"),
        firstAidInstructions: text("firstAidInstructions"),
        emergencyContacts: contacts.filter((c) => c.name.trim() || c.phone?.trim()),
        source: text("source"),
        notes: text("notes"),
      },
      reviewDueOn: text("reviewDueOn"),
      reason: text("reason") ?? "",
      expectedVersion: record?.version ?? null,
    });
  };

  const d = record?.data ?? {};
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={record ? "Editar ficha de saúde" : "Nova ficha de saúde"}
      description="Salvar cria uma versão nova; a anterior continua no histórico."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="health-form" disabled={save.isPending}>
            {save.isPending ? "Salvando…" : "Salvar versão"}
          </Button>
        </>
      }
    >
      <StepUpDialog open={stepUp} />
      <form id="health-form" key={record?.version ?? 0} onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Tipo sanguíneo (se comprovado)" name="bloodType" defaultValue={d.bloodType ?? ""} maxLength={10} error={errors.bloodType} />
          <Input label="Revisar em" name="reviewDueOn" type="date" defaultValue={record?.reviewDueOn ?? ""} />
        </div>
        <Textarea label="Alergias e cuidados" name="allergies" defaultValue={d.allergies ?? ""} error={errors.allergies} />
        <Textarea label="Condições relevantes para a escola" name="conditions" defaultValue={d.conditions ?? ""} error={errors.conditions} />
        <Textarea label="Medicamentos informados" name="medications" defaultValue={d.medications ?? ""} hint="Como informado pela família ou receita. A escola não prescreve." error={errors.medications} />
        <Textarea label="Restrições e adaptações" name="restrictions" defaultValue={d.restrictions ?? ""} error={errors.restrictions} />
        <Textarea label="Primeiros cuidados autorizados" name="firstAidInstructions" defaultValue={d.firstAidInstructions ?? ""} error={errors.firstAidInstructions} />

        <fieldset className="space-y-2">
          <legend className="text-sm font-medium text-foreground">Contatos de emergência</legend>
          {contacts.map((c, i) => (
            <div key={i} className="grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] items-end gap-2">
              <Input label="Nome" value={c.name} onChange={(e) => setContacts(contacts.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} />
              <Input label="Telefone" value={c.phone ?? ""} onChange={(e) => setContacts(contacts.map((x, j) => (j === i ? { ...x, phone: e.target.value } : x)))} />
              <Button size="icon" variant="ghost" aria-label="Remover contato" onClick={() => setContacts(contacts.filter((_, j) => j !== i))}>
                <Trash2 />
              </Button>
            </div>
          ))}
          <Button size="sm" variant="ghost" onClick={() => setContacts([...contacts, { name: "", phone: "" }])}>
            <Plus />
            Adicionar contato
          </Button>
          {errors.emergencyContacts ? <p className="text-xs text-critical-text">{errors.emergencyContacts}</p> : null}
        </fieldset>

        <div className="grid gap-4 sm:grid-cols-2">
          <Input label="Origem da informação" name="source" defaultValue={d.source ?? ""} placeholder="Ex.: ficha preenchida pela mãe" error={errors.source} />
          <Input label="Observações" name="notes" defaultValue={d.notes ?? ""} error={errors.notes} />
        </div>
        <Textarea label="Motivo da alteração" name="reason" rows={2} required error={errors.reason} hint="Obrigatório. Fica no histórico, cifrado." />
      </form>
    </Dialog>
  );
}
