import { api, apiFetch, ApiError } from "@sinedu/ui";

/**
 * Saúde e Proteção/Sigilo. Este módulo só é carregado (import dinâmico) por telas de Direção e Pedagogia,
 * e nenhuma chamada daqui acontece antes de a pessoa abrir a aba correspondente.
 */

export interface EmergencyContact {
  name: string;
  phone?: string | null;
  relationship?: string | null;
}

export interface HealthData {
  bloodType?: string | null;
  allergies?: string | null;
  conditions?: string | null;
  medications?: string | null;
  restrictions?: string | null;
  firstAidInstructions?: string | null;
  emergencyContacts?: EmergencyContact[] | null;
  source?: string | null;
  notes?: string | null;
}

export interface HealthView {
  id: string;
  version: number;
  reviewDueOn: string | null;
  data: HealthData;
  updatedAt: string;
  updatedByName: string;
}

export type ProtectionKind =
  | "Guarda" | "Tutela" | "MedidaProtetiva" | "RestricaoJudicial" | "AutorizacaoRetirada" | "ProtecaoCriancaAdolescente" | "Outro";
export type VerificationLevel = "Documentado" | "Declarado" | "NaoVerificado";
export type ProtectionStatus = "Vigente" | "Vencida" | "Revogada";

export interface PickupPerson {
  name: string;
  document?: string | null;
  relationship?: string | null;
  note?: string | null;
}

export interface ProtectionData {
  title: string;
  description?: string | null;
  authority?: string | null;
  documentNumber?: string | null;
  issuedOn?: string | null;
  validFrom?: string | null;
  basis?: string | null;
  verification: VerificationLevel;
  authorizedPeople?: PickupPerson[] | null;
  unauthorizedPeople?: PickupPerson[] | null;
  notes?: string | null;
}

export interface ProtectionView {
  id: string;
  kind: ProtectionKind;
  status: ProtectionStatus;
  validUntil: string | null;
  version: number;
  data: ProtectionData;
  updatedAt: string;
  updatedByName: string;
}

export interface VersionInfo {
  version: number;
  createdAt: string;
  createdByName: string;
}

export interface VersionView<T> extends VersionInfo {
  reason: string;
  data: T;
}

export interface AttachmentView {
  id: string;
  ownerKind: "Health" | "Protection";
  ownerId: string;
  fileName: string;
  contentType: string;
  description: string | null;
  sizeBytes: number;
  createdAt: string;
  createdByName: string;
}

export interface PendingItem {
  studentId: string;
  studentName: string;
  classroom: string | null;
  type: "health_review" | "protection_expiring" | "protection_expired";
  recordId: string;
  kind: ProtectionKind | null;
  dueOn: string;
}

export interface HealthImportRow {
  rowNumber: number;
  status: "new" | "update" | "unchanged" | "not_found" | "no_code" | "duplicate";
  studentId: string | null;
  studentName: string | null;
  fields: string[];
}

export interface HealthImportResult {
  sheet: string;
  rows: number;
  new: number;
  update: number;
  unchanged: number;
  notFound: number;
  noCode: number;
  duplicate: number;
  applied: number;
  dryRun: boolean;
  items: HealthImportRow[];
}

export const protectionKindLabels: Record<ProtectionKind, string> = {
  Guarda: "Guarda",
  Tutela: "Tutela",
  MedidaProtetiva: "Medida protetiva",
  RestricaoJudicial: "Restrição judicial",
  AutorizacaoRetirada: "Autorização de retirada",
  ProtecaoCriancaAdolescente: "Proteção da criança/adolescente",
  Outro: "Outro",
};

export const verificationLabels: Record<VerificationLevel, string> = {
  Documentado: "Documento conferido",
  Declarado: "Declarado, sem documento",
  NaoVerificado: "Não verificado",
};

export const importStatusLabels: Record<HealthImportRow["status"], string> = {
  new: "Nova ficha",
  update: "Atualização",
  unchanged: "Sem alteração",
  not_found: "CGM não encontrado",
  no_code: "Sem CGM",
  duplicate: "CGM repetido",
};

/** A API pede autenticação forte recente (≤ 15 min) para estes dados. */
export const isStepUpRequired = (error: unknown) => error instanceof ApiError && error.status === 401 && error.code === "step_up_required";

const base = (studentId: string) => `/api/students/${studentId}`;

/** Baixa um arquivo da API como blob (mantém erros de step-up tratáveis) e dispara o download. */
async function download(path: string, init: RequestInit, fallbackName: string) {
  const response = await fetch(path, { ...init, credentials: "same-origin" });
  if (!response.ok) {
    let problem: { title?: string; code?: string; errors?: Record<string, string[]> } = {};
    try {
      problem = await response.json();
    } catch {
      // sem corpo
    }
    const first = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined;
    throw new ApiError(response.status, problem.code, first ?? problem.title ?? "Não foi possível baixar.", problem.errors ?? {});
  }

  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get("Content-Disposition") ?? "")?.[1];
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = name ? decodeURIComponent(name) : fallbackName;
  a.hidden = true;
  document.body.appendChild(a);
  a.click();
  a.remove();
  // Revogar na hora cancela o download em alguns navegadores; o blob fica só até o download começar.
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
}

export const protectedApi = {
  health: (studentId: string) => api.get<{ record: HealthView | null }>(`${base(studentId)}/health`),
  saveHealth: (studentId: string, body: { data: HealthData; reviewDueOn: string | null; reason: string; expectedVersion: number | null }) =>
    api.put<HealthView>(`${base(studentId)}/health`, body),
  healthVersions: (studentId: string) => api.get<VersionInfo[]>(`${base(studentId)}/health/versions`),
  healthVersion: (studentId: string, version: number) => api.get<VersionView<HealthData>>(`${base(studentId)}/health/versions/${version}`),

  protection: (studentId: string) => api.get<ProtectionView[]>(`${base(studentId)}/protection`),
  createProtection: (studentId: string, body: { kind: ProtectionKind; validUntil: string | null; data: ProtectionData; reason: string }) =>
    api.post<ProtectionView>(`${base(studentId)}/protection`, body),
  updateProtection: (
    studentId: string,
    recordId: string,
    body: { kind: ProtectionKind; validUntil: string | null; data: ProtectionData; reason: string; expectedVersion: number },
  ) => api.put<ProtectionView>(`${base(studentId)}/protection/${recordId}`, body),
  revokeProtection: (studentId: string, recordId: string, reason: string, expectedVersion: number) =>
    api.post<ProtectionView>(`${base(studentId)}/protection/${recordId}/revoke`, { reason, expectedVersion }),
  protectionVersions: (studentId: string, recordId: string) => api.get<VersionInfo[]>(`${base(studentId)}/protection/${recordId}/versions`),
  protectionVersion: (studentId: string, recordId: string, version: number) =>
    api.get<VersionView<ProtectionData>>(`${base(studentId)}/protection/${recordId}/versions/${version}`),

  attachments: (studentId: string, owner: "Health" | "Protection", ownerId: string) =>
    api.get<AttachmentView[]>(`${base(studentId)}/protected-attachments?owner=${owner}&ownerId=${ownerId}`),
  upload: (studentId: string, owner: "Health" | "Protection", ownerId: string, file: File, description: string) => {
    const body = new FormData();
    body.append("file", file);
    body.append("owner", owner);
    body.append("ownerId", ownerId);
    if (description) body.append("description", description);
    return apiFetch<AttachmentView>(`${base(studentId)}/protected-attachments`, { method: "POST", body });
  },
  downloadAttachment: (studentId: string, attachment: AttachmentView) =>
    download(`${base(studentId)}/protected-attachments/${attachment.id}`, {}, attachment.fileName),
  removeAttachment: (studentId: string, attachmentId: string) => api.post(`${base(studentId)}/protected-attachments/${attachmentId}/remove`),

  exportPdf: (studentId: string, body: { health: boolean; protection: boolean; justification: string }) =>
    download(
      `${base(studentId)}/protected-export`,
      { method: "POST", body: JSON.stringify(body), headers: { "Content-Type": "application/json", Accept: "application/pdf, application/json" } },
      "exportacao-protegida.pdf",
    ),

  pending: () => api.get<PendingItem[]>("/api/protected/pending"),
  healthImportColumns: () => api.get<string[]>("/api/protected/health-import/columns"),
  healthImport: (file: File, confirm: boolean, reason?: string) => {
    const body = new FormData();
    body.append("file", file);
    body.append("confirm", confirm ? "true" : "false");
    if (reason) body.append("reason", reason);
    return apiFetch<HealthImportResult>("/api/protected/health-import", { method: "POST", body });
  },
};
