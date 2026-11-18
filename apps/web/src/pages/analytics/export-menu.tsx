import { useMutation } from "@tanstack/react-query";
import { Alert, Button, Dialog } from "@sinedu/ui";
import { Download, FileSpreadsheet, FileText } from "lucide-react";
import { useState } from "react";
import { analyticsApi, type AnalyticsFilter } from "../../lib/analytics-api";

/** Exportação dos indicadores (CSV ou PDF). Só agregados; contagens de 1 a 4 saem como "<5" (D11). */
export function ExportMenu({ filter }: { filter: AnalyticsFilter }) {
  const [open, setOpen] = useState(false);
  const run = useMutation({ mutationFn: (format: "csv" | "pdf") => analyticsApi.export(filter, format), onSuccess: () => setOpen(false) });

  return (
    <>
      <Button onClick={() => setOpen(true)}>
        <Download />
        Exportar
      </Button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Exportar indicadores"
        description="O arquivo traz só números agregados do recorte atual: sem nomes de alunos, sem descrições, sem dados de saúde ou proteção. Contagens de 1 a 4 aparecem como <5."
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <Button size="lg" onClick={() => run.mutate("csv")} disabled={run.isPending}>
            <FileSpreadsheet />
            Planilha (CSV)
          </Button>
          <Button size="lg" onClick={() => run.mutate("pdf")} disabled={run.isPending}>
            <FileText />
            Relatório (PDF)
          </Button>
        </div>
        {run.error ? <Alert tone="critical" className="mt-3" title="Não foi possível exportar." /> : null}
      </Dialog>
    </>
  );
}
