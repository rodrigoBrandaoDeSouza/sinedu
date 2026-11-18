import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Card, CardBody, CardHeader, PageHeader, StatTile, Table, Td, Textarea, Th, Tr } from "@sinedu/ui";
import { CheckCircle2, FileSpreadsheet, ShieldCheck, Upload } from "lucide-react";
import { useRef, useState } from "react";
import { Link } from "react-router";
import { importStatusLabels, isStepUpRequired, protectedApi, type HealthImportResult, type HealthImportRow } from "../../lib/protected-api";
import { StepUpDialog } from "./shared";
import { errorsOf } from "./utils";

const statusTone: Record<HealthImportRow["status"], "good" | "info" | "neutral" | "warning" | "critical"> = {
  new: "good",
  update: "info",
  unchanged: "neutral",
  not_found: "warning",
  no_code: "warning",
  duplicate: "critical",
};

/**
 * F4-05: importação de Saúde separada da importação comum. O arquivo é lido e descartado a cada envio;
 * a prévia mostra só quais colunas vieram preenchidas, nunca os valores. Aluno só por CGM.
 */
export default function HealthImportPage() {
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File>();
  const [preview, setPreview] = useState<HealthImportResult>();
  const [done, setDone] = useState<HealthImportResult>();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [stepUp, setStepUp] = useState(false);
  const columns = useQuery({ queryKey: ["protected", "health-import", "columns"], queryFn: protectedApi.healthImportColumns, retry: false });

  const onError = (err: unknown) => (isStepUpRequired(err) ? setStepUp(true) : setErrors(errorsOf(err)));
  const check = useMutation({ mutationFn: (f: File) => protectedApi.healthImport(f, false), onSuccess: setPreview, onError });
  const confirm = useMutation({ mutationFn: (reason: string) => protectedApi.healthImport(file!, true, reason), onSuccess: setDone, onError });

  const pick = (f?: File) => {
    if (!f) return;
    setFile(f);
    setPreview(undefined);
    setDone(undefined);
    setErrors({});
    check.mutate(f);
  };

  return (
    <>
      <PageHeader title="Importar fichas de saúde" description="Fluxo exclusivo de Direção e Pedagogia. O arquivo não é guardado em lugar nenhum." />
      <StepUpDialog open={stepUp || isStepUpRequired(columns.error)} />

      {done ? (
        <Card className="p-6 text-center">
          <CheckCircle2 className="mx-auto mb-2 size-8 text-good-text" aria-hidden />
          <p className="font-medium text-foreground">{done.applied} fichas gravadas como nova versão.</p>
          <p className="mt-1 text-sm text-muted-foreground">Cada ficha tem o motivo informado no histórico.</p>
          <Link to="/protegido/pendencias" className="mt-3 inline-block text-sm text-primary hover:underline">
            Ver pendências
          </Link>
        </Card>
      ) : (
        <div className="space-y-4">
          <Card className="p-5">
            {errors.form ? <Alert tone="critical" className="mb-4" title={errors.form} /> : null}
            {errors.file ? <Alert tone="critical" className="mb-4" title={errors.file} /> : null}
            <div className="flex flex-col items-center justify-center rounded-xl border-2 border-dashed border-border-strong px-6 py-8 text-center">
              <FileSpreadsheet className="mb-2 size-8 text-muted-foreground" aria-hidden />
              <p className="font-medium text-foreground">{check.isPending ? "Conferindo…" : (file?.name ?? "Planilha .xlsx ou .csv")}</p>
              <p className="mt-1 max-w-xl text-sm text-muted-foreground">
                Precisa da coluna <strong>CGM</strong> e de pelo menos uma destas: {columns.data?.filter((c) => c !== "CGM").join(", ") ?? "…"}.
                Linhas sem CGM não são importadas (nunca usamos o nome para achar o aluno).
              </p>
              <Button className="mt-4" variant="primary" disabled={check.isPending} onClick={() => input.current?.click()}>
                <Upload />
                Escolher arquivo
              </Button>
              <input ref={input} type="file" accept=".xlsx,.csv" className="hidden" onChange={(e) => pick(e.target.files?.[0])} />
            </div>
            <p className="mt-3 flex items-center gap-1.5 text-xs text-muted-foreground">
              <ShieldCheck className="size-3.5" aria-hidden />
              Campo vazio na planilha não apaga o que já existe. Cada ficha alterada vira uma versão nova, cifrada.
            </p>
          </Card>

          {preview ? (
            <>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
                <StatTile label="Novas fichas" value={preview.new} />
                <StatTile label="Atualizações" value={preview.update} />
                <StatTile label="Sem alteração" value={preview.unchanged} />
                <StatTile label="CGM não encontrado" value={preview.notFound} />
                <StatTile label="Sem CGM" value={preview.noCode} />
                <StatTile label="CGM repetido" value={preview.duplicate} />
              </div>

              <Card>
                <CardHeader title={`Prévia · aba ${preview.sheet}`} description="Mostramos só quais campos vieram preenchidos." />
                <CardBody>
                  <Table>
                    <thead>
                      <tr>
                        <Th>Linha</Th>
                        <Th>Aluno</Th>
                        <Th>Situação</Th>
                        <Th>Campos preenchidos</Th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.items.map((r) => (
                        <Tr key={r.rowNumber}>
                          <Td className="tabular-nums">{r.rowNumber}</Td>
                          <Td>{r.studentName ?? <span className="text-muted-foreground">—</span>}</Td>
                          <Td>
                            <Badge tone={statusTone[r.status]} icon>
                              {importStatusLabels[r.status]}
                            </Badge>
                          </Td>
                          <Td className="text-foreground-secondary">{r.fields.join(", ") || "—"}</Td>
                        </Tr>
                      ))}
                    </tbody>
                  </Table>
                </CardBody>
              </Card>

              <Card className="p-5">
                <form
                  className="space-y-3"
                  noValidate
                  onSubmit={(e) => {
                    e.preventDefault();
                    setErrors({});
                    confirm.mutate(String(new FormData(e.currentTarget).get("reason") ?? "").trim());
                  }}
                >
                  <Textarea label="Motivo" name="reason" rows={2} error={errors.reason} hint="Ex.: fichas de saúde entregues pelas famílias em novembro. Vai para o histórico de cada ficha." />
                  <Button variant="primary" type="submit" disabled={confirm.isPending || preview.new + preview.update === 0}>
                    {confirm.isPending ? "Gravando…" : `Gravar ${preview.new + preview.update} fichas`}
                  </Button>
                  {confirm.error && !(confirm.error instanceof ApiError && confirm.error.status === 400) && !isStepUpRequired(confirm.error) ? (
                    <Alert tone="critical" title="Não foi possível gravar." />
                  ) : null}
                </form>
              </Card>
            </>
          ) : null}
        </div>
      )}
    </>
  );
}
