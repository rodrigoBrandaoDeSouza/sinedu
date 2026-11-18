import { Alert } from "@sinedu/ui";
import { ExportButton } from "./shared";
import { HealthTab } from "./health-tab";
import { ProtectionTab } from "./protection-tab";

/**
 * Carregado com import() só quando Direção/Pedagogia abre a aba: o código e as chamadas
 * dos módulos protegidos não existem para os demais perfis.
 */
export default function ProtectedPanel({ studentId, tab }: { studentId: string; tab: "saude" | "protecao" }) {
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <Alert tone="info" className="flex-1" title="Dados sigilosos">
          Visíveis só para Direção e Pedagogia desta escola. Cada acesso fica registrado na auditoria.
        </Alert>
        <ExportButton studentId={studentId} />
      </div>
      {tab === "saude" ? <HealthTab studentId={studentId} /> : <ProtectionTab studentId={studentId} />}
    </div>
  );
}
