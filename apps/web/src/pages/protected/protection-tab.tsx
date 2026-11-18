import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Badge, Button, Card, Dialog, EmptyState, Input, Select, Textarea } from "@sinedu/ui";
import { Ban, Pencil, Plus, ShieldAlert } from "lucide-react";
import { useState, type FormEvent } from "react";
import { formatDate } from "../../lib/school-api";
import {
  isStepUpRequired,
  protectedApi,
  protectionKindLabels,
  verificationLabels,
  type PickupPerson,
  type ProtectionData,
  type ProtectionKind,
  type ProtectionStatus,
  type ProtectionView,
  type VerificationLevel,
} from "../../lib/protected-api";
import { Attachments, ProtectedState, Section, StepUpDialog, UpdatedBy, VersionHistory } from "./shared";
import { errorsOf } from "./utils";

const statusTone: Record<ProtectionStatus, "good" | "warning" | "neutral"> = { Vigente: "good", Vencida: "warning", Revogada: "neutral" };

export function ProtectionTab({ studentId }: { studentId: string }) {
  const key = ["protected", studentId, "protection"];
  const list = useQuery({ queryKey: key, queryFn: () => protectedApi.protection(studentId), retry: false });
  const [editing, setEditing] = useState<ProtectionView | "new">();
  const [revoking, setRevoking] = useState<ProtectionView>();

  return (
    <ProtectedState error={list.error}>
      <div className="mb-3 flex justify-end">
        <Button variant="primary" onClick={() => setEditing("new")}>
          <Plus />
          Novo registro
        </Button>
      </div>
      {list.isPending ? (
        <div className="h-40 animate-pulse rounded-(--radius-card) border border-border bg-surface" aria-busy="true" />
      ) : list.data?.length === 0 ? (
        <EmptyState
          icon={ShieldAlert}
          title="Nenhum registro de proteção"
          description="Guarda, tutela, medidas protetivas e pessoas autorizadas ou impedidas de retirar o aluno, com documento e vigência."
        />
      ) : (
        <div className="space-y-4">
          {list.data?.map((r) => (
            <Card key={r.id} className="space-y-4 p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="font-semibold text-foreground">{r.data.title}</h3>
                    <Badge tone={statusTone[r.status]} icon>
                      {r.status}
                    </Badge>
                    <Badge tone="outline">{protectionKindLabels[r.kind]}</Badge>
                  </div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {r.validUntil ? `Válido até ${formatDate(r.validUntil)}` : "Sem data de término"} · {verificationLabels[r.data.verification]}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <UpdatedBy name={r.updatedByName} at={r.updatedAt} version={r.version} />
                  <VersionHistory
                    queryKey={[...key, r.id]}
                    list={() => protectedApi.protectionVersions(studentId, r.id)}
                    load={(v) => protectedApi.protectionVersion(studentId, r.id, v)}
                    render={(d) => <ProtectionDetails data={d} />}
                  />
                  {r.status !== "Revogada" ? (
                    <>
                      <Button size="sm" onClick={() => setEditing(r)}>
                        <Pencil />
                        Editar
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => setRevoking(r)}>
                        <Ban />
                        Revogar
                      </Button>
                    </>
                  ) : null}
                </div>
              </div>
              <ProtectionDetails data={r.data} />
              <Attachments studentId={studentId} owner="Protection" ownerId={r.id} />
            </Card>
          ))}
        </div>
      )}
      <ProtectionDialog
        key={editing === "new" ? "new" : `${editing?.id}-${editing?.version}`}
        studentId={studentId}
        record={editing === "new" ? null : (editing ?? null)}
        open={editing !== undefined}
        onClose={() => setEditing(undefined)}
      />
      <RevokeDialog studentId={studentId} record={revoking} onClose={() => setRevoking(undefined)} />
    </ProtectedState>
  );
}

const people = (list?: PickupPerson[] | null) =>
  list?.length
    ? list.map((p) => `${p.name}${p.relationship ? ` (${p.relationship})` : ""}${p.document ? ` · doc. ${p.document}` : ""}${p.note ? ` — ${p.note}` : ""}`).join("\n")
    : null;

export function ProtectionDetails({ data }: { data: ProtectionData }) {
  return (
    <dl className="grid gap-x-6 gap-y-4 sm:grid-cols-2">
      <Section label="Descrição" value={data.description} />
      <Section label="Autoridade / origem" value={data.authority} />
      <Section label="Documento" value={[data.documentNumber, data.issuedOn ? `emitido em ${formatDate(data.issuedOn)}` : null].filter(Boolean).join(" · ")} />
      <Section label="Início da vigência" value={data.validFrom ? formatDate(data.validFrom) : null} />
      <Section label="Fundamentação" value={data.basis} />
      <Section label="Observações" value={data.notes} />
      <Section label="Autorizados a retirar" value={people(data.authorizedPeople)} />
      <Section label="Impedidos de retirar" value={people(data.unauthorizedPeople)} />
    </dl>
  );
}

/** Uma pessoa por linha: "Nome; documento; parentesco; observação". */
const parsePeople = (text: string): PickupPerson[] =>
  text
    .split("\n")
    .map((line) => line.split(";").map((p) => p.trim()))
    .filter(([name]) => name)
    .map(([name, document, relationship, note]) => ({ name: name!, document: document || null, relationship: relationship || null, note: note || null }));

const formatPeople = (list?: PickupPerson[] | null) =>
  (list ?? []).map((p) => [p.name, p.document ?? "", p.relationship ?? "", p.note ?? ""].join("; ").replace(/(; )+$/, "")).join("\n");

function ProtectionDialog({ studentId, record, open, onClose }: { studentId: string; record: ProtectionView | null; open: boolean; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [stepUp, setStepUp] = useState(false);
  const save = useMutation({
    mutationFn: (body: { kind: ProtectionKind; validUntil: string | null; data: ProtectionData; reason: string }) =>
      record ? protectedApi.updateProtection(studentId, record.id, { ...body, expectedVersion: record.version }) : protectedApi.createProtection(studentId, body),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["protected", studentId, "protection"] });
      onClose();
    },
    onError: (err) => (isStepUpRequired(err) ? setStepUp(true) : setErrors(errorsOf(err))),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const text = (k: string) => String(f.get(k) ?? "").trim() || null;
    setErrors({});
    save.mutate({
      kind: f.get("kind") as ProtectionKind,
      validUntil: text("validUntil"),
      reason: text("reason") ?? "",
      data: {
        title: text("title") ?? "",
        description: text("description"),
        authority: text("authority"),
        documentNumber: text("documentNumber"),
        issuedOn: text("issuedOn"),
        validFrom: text("validFrom"),
        basis: text("basis"),
        verification: f.get("verification") as VerificationLevel,
        authorizedPeople: parsePeople(String(f.get("authorizedPeople") ?? "")),
        unauthorizedPeople: parsePeople(String(f.get("unauthorizedPeople") ?? "")),
        notes: text("notes"),
      },
    });
  };

  const d = record?.data;
  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={record ? "Editar registro de proteção" : "Novo registro de proteção"}
      description="Registre só o necessário à proteção do aluno, com fonte e nível de verificação. Evite alegações não verificadas."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="protection-form" disabled={save.isPending}>
            {save.isPending ? "Salvando…" : record ? "Salvar versão" : "Criar registro"}
          </Button>
        </>
      }
    >
      <StepUpDialog open={stepUp} />
      <form id="protection-form" onSubmit={submit} className="space-y-4" noValidate>
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <Input label="Título" name="title" defaultValue={d?.title ?? ""} maxLength={200} error={errors.title} placeholder="Ex.: Guarda unilateral" />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select label="Tipo" name="kind" defaultValue={record?.kind ?? "Guarda"} options={Object.entries(protectionKindLabels).map(([value, label]) => ({ value, label }))} />
          <Select label="Verificação" name="verification" defaultValue={d?.verification ?? "Documentado"} options={Object.entries(verificationLabels).map(([value, label]) => ({ value, label }))} />
          <Input label="Início da vigência" name="validFrom" type="date" defaultValue={d?.validFrom ?? ""} />
          <Input label="Válido até" name="validUntil" type="date" defaultValue={record?.validUntil ?? ""} error={errors.validUntil} />
          <Input label="Autoridade / origem" name="authority" defaultValue={d?.authority ?? ""} placeholder="Ex.: Vara da Infância" error={errors.authority} />
          <Input label="Nº do documento" name="documentNumber" defaultValue={d?.documentNumber ?? ""} error={errors.documentNumber} />
          <Input label="Emitido em" name="issuedOn" type="date" defaultValue={d?.issuedOn ?? ""} />
        </div>
        <Textarea label="Descrição" name="description" defaultValue={d?.description ?? ""} error={errors.description} />
        <Textarea label="Fundamentação" name="basis" defaultValue={d?.basis ?? ""} error={errors.basis} />
        <Textarea
          label="Autorizados a retirar"
          name="authorizedPeople"
          defaultValue={formatPeople(d?.authorizedPeople)}
          hint="Uma pessoa por linha: Nome; documento; parentesco; observação"
          error={errors.authorizedPeople}
        />
        <Textarea
          label="Impedidos de retirar"
          name="unauthorizedPeople"
          defaultValue={formatPeople(d?.unauthorizedPeople)}
          hint="Uma pessoa por linha: Nome; documento; parentesco; observação"
          error={errors.unauthorizedPeople}
        />
        <Textarea label="Observações" name="notes" defaultValue={d?.notes ?? ""} error={errors.notes} />
        <Textarea label="Motivo da alteração" name="reason" rows={2} error={errors.reason} hint="Obrigatório. Fica no histórico, cifrado." />
      </form>
    </Dialog>
  );
}

function RevokeDialog({ studentId, record, onClose }: { studentId: string; record?: ProtectionView; onClose: () => void }) {
  const client = useQueryClient();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [stepUp, setStepUp] = useState(false);
  const revoke = useMutation({
    mutationFn: (reason: string) => protectedApi.revokeProtection(studentId, record!.id, reason, record!.version),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["protected", studentId, "protection"] });
      onClose();
    },
    onError: (err) => (isStepUpRequired(err) ? setStepUp(true) : setErrors(errorsOf(err))),
  });

  return (
    <Dialog
      open={record !== undefined}
      onClose={onClose}
      title="Revogar registro?"
      description="O registro deixa de valer, mas continua no histórico com o motivo. Não é possível desfazer: crie um novo registro se precisar."
      footer={
        <>
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="danger" type="submit" form="revoke-form" disabled={revoke.isPending}>
            Revogar
          </Button>
        </>
      }
    >
      <StepUpDialog open={stepUp} />
      <form
        id="revoke-form"
        onSubmit={(e) => {
          e.preventDefault();
          setErrors({});
          revoke.mutate(String(new FormData(e.currentTarget).get("reason") ?? "").trim());
        }}
        noValidate
        className="space-y-3"
      >
        {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
        <p className="text-sm font-medium text-foreground">{record?.data.title}</p>
        <Textarea label="Motivo da revogação" name="reason" rows={2} error={errors.reason} />
      </form>
    </Dialog>
  );
}
