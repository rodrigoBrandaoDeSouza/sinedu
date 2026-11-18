import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Card, EmptyState, PageHeader, Table, Td, Th, Tr } from "@sinedu/ui";
import { FileSpreadsheet, ShieldCheck, Upload } from "lucide-react";
import { useRef, useState, type DragEvent } from "react";
import { useNavigate } from "react-router";
import { importsApi, statusLabels, type ImportStatus } from "../lib/imports-api";

const statusTone: Record<ImportStatus, "good" | "info" | "warning" | "critical" | "neutral" | "brand"> = {
  Uploaded: "warning",
  Validated: "info",
  Processing: "brand",
  Completed: "good",
  Cancelled: "neutral",
  Failed: "critical",
};

export function ImportsPage() {
  const navigate = useNavigate();
  const history = useQuery({ queryKey: ["imports"], queryFn: importsApi.list });

  return (
    <>
      <PageHeader title="Importações" description="Traga alunos, turmas e responsáveis de planilhas (.xlsx ou .csv) sem recadastrar ninguém." />

      <UploadCard onUploaded={(id) => navigate(`/importacoes/${id}`)} />

      <h2 className="mb-3 mt-8 text-sm font-semibold text-foreground-secondary">Histórico</h2>
      {history.data?.length === 0 ? (
        <EmptyState icon={FileSpreadsheet} title="Nenhuma importação ainda" description="As importações feitas aparecem aqui, com o resultado de cada uma." />
      ) : (
        <Card>
          <Table>
            <thead>
              <tr>
                <Th>Arquivo</Th>
                <Th>Situação</Th>
                <Th>Resultado</Th>
                <Th>Enviado em</Th>
              </tr>
            </thead>
            <tbody>
              {history.data?.map((b) => (
                <Tr key={b.id} className="cursor-pointer" onClick={() => navigate(`/importacoes/${b.id}`)}>
                  <Td>
                    <p className="font-medium">{b.fileName}</p>
                    {b.sheet ? <p className="text-xs text-muted-foreground">Aba {b.sheet}</p> : null}
                  </Td>
                  <Td>
                    <Badge tone={statusTone[b.status]} icon={b.status === "Completed"}>
                      {statusLabels[b.status]}
                    </Badge>
                  </Td>
                  <Td className="text-foreground-secondary">
                    {b.totals
                      ? b.status === "Completed"
                        ? `${b.totals.created} criados · ${b.totals.updated} atualizados · ${b.totals.skipped} ignorados`
                        : `${b.totals.new} novos · ${b.totals.update} atualizações · ${b.totals.pending} para revisar`
                      : "—"}
                  </Td>
                  <Td className="text-foreground-secondary tabular-nums">{new Date(b.createdAt).toLocaleString("pt-BR")}</Td>
                </Tr>
              ))}
            </tbody>
          </Table>
        </Card>
      )}
    </>
  );
}

function UploadCard({ onUploaded }: { onUploaded: (id: string) => void }) {
  const input = useRef<HTMLInputElement>(null);
  const [dragging, setDragging] = useState(false);
  const upload = useMutation({ mutationFn: importsApi.upload, onSuccess: (r) => onUploaded(r.id) });

  const pick = (files: FileList | null) => {
    const file = files?.[0];
    if (file) upload.mutate(file);
  };

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDragging(false);
    pick(e.dataTransfer.files);
  };

  return (
    <Card className="p-5">
      {upload.error ? (
        <Alert tone="critical" className="mb-4" title={upload.error instanceof ApiError ? upload.error.message : "Não foi possível enviar."} />
      ) : null}
      <div
        onDragOver={(e) => {
          e.preventDefault();
          setDragging(true);
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
        className={`flex flex-col items-center justify-center rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors ${
          dragging ? "border-primary bg-primary-soft/50" : "border-border-strong"
        }`}
      >
        <span className="mb-3 grid size-12 place-items-center rounded-2xl bg-primary-soft text-primary-soft-foreground">
          <Upload className="size-6" aria-hidden />
        </span>
        <p className="font-medium text-foreground">{upload.isPending ? "Lendo a planilha…" : "Arraste a planilha aqui"}</p>
        <p className="mt-1 text-sm text-muted-foreground">.xlsx ou .csv, até 10 MB. Relatórios do SERE e planilhas da secretaria funcionam.</p>
        <Button className="mt-4" variant="primary" disabled={upload.isPending} onClick={() => input.current?.click()}>
          Escolher arquivo
        </Button>
        <input ref={input} type="file" accept=".xlsx,.csv" className="hidden" onChange={(e) => pick(e.target.files)} />
      </div>
      <p className="mt-3 flex items-center gap-1.5 text-xs text-muted-foreground">
        <ShieldCheck className="size-3.5" aria-hidden />
        O arquivo não é guardado. As linhas ficam disponíveis para revisão por 90 dias e depois são apagadas. Dados de saúde e proteção nunca entram por aqui.
      </p>
    </Card>
  );
}
