import { api, apiFetch, ApiError } from "@sinedu/ui";
import type { Page, Relationship } from "./school-api";

export type OccurrenceStatus = "Rascunho" | "Registrada" | "EmAcompanhamento" | "Concluida" | "Cancelada";
export type NotificationStatus = "Pendente" | "Enviado" | "Falhou" | "Cancelado";

export interface Category {
  id: string;
  name: string;
  isActive: boolean;
  sortOrder: number;
}

export interface Severity {
  id: string;
  name: string;
  sortOrder: number;
}

export interface OccurrenceListItem {
  id: string;
  studentId: string;
  studentName: string;
  studentCode: string | null;
  classroomId: string;
  classroom: string;
  occurredAt: string;
  category: string;
  severity: string | null;
  status: OccurrenceStatus;
  createdByName: string;
  registeredAt: string | null;
  version: number;
}

export interface RevisionInfo {
  version: number;
  action: string;
  reason: string;
  changedByName: string;
  changedAt: string;
}

export interface AttachmentInfo {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedByName: string;
  uploadedAt: string;
}

export interface OccurrenceDetails {
  id: string;
  studentId: string;
  studentName: string;
  studentCode: string | null;
  classroomId: string;
  classroom: string;
  schoolYear: number | null;
  occurredAt: string;
  categoryId: string;
  category: string;
  severityId: string | null;
  severity: string | null;
  location: string | null;
  description: string;
  involved: string | null;
  actionsTaken: string | null;
  referrals: string | null;
  notes: string | null;
  status: OccurrenceStatus;
  createdByName: string;
  createdAt: string;
  registeredByName: string | null;
  registeredAt: string | null;
  concludedAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  version: number;
  isAuthor: boolean;
  revisions: RevisionInfo[];
  attachments: AttachmentInfo[];
}

export interface OccurrenceInput {
  studentId: string;
  occurredAt: string;
  categoryId: string;
  severityId: string | null;
  location: string | null;
  description: string;
  involved: string | null;
  actionsTaken: string | null;
  referrals: string | null;
  notes: string | null;
}

export interface Template {
  id: string;
  channel: "Email" | "WhatsApp";
  name: string;
  subject: string;
  body: string;
  isActive: boolean;
}

export interface RecipientPreview {
  guardianId: string;
  name: string;
  relationship: Relationship;
  email: string | null;
  eligible: boolean;
  reason: string | null;
  subject: string | null;
  body: string | null;
}

export interface CommunicationPreview {
  occurrenceId: string;
  occurrenceStatus: OccurrenceStatus;
  channels: { channel: "Email" | "WhatsApp"; enabled: boolean; reason: string | null }[];
  templates: Template[];
  templateId: string;
  recipients: RecipientPreview[];
}

export interface NotificationView {
  id: string;
  occurrenceId: string;
  studentId: string;
  recipientName: string;
  recipientAddress: string;
  channel: "Email" | "WhatsApp";
  templateName: string;
  subject: string;
  body: string;
  status: NotificationStatus;
  attempts: number;
  nextAttemptAt: string | null;
  sentAt: string | null;
  lastError: string | null;
  authorizedByName: string;
  authorizedAt: string;
  history: { attemptNo: number; startedAt: string; succeeded: boolean; error: string | null; trigger: string }[];
}

export const occurrenceStatusLabels: Record<OccurrenceStatus, string> = {
  Rascunho: "Rascunho",
  Registrada: "Registrada",
  EmAcompanhamento: "Em acompanhamento",
  Concluida: "Concluída",
  Cancelada: "Cancelada",
};

export const occurrenceStatusTone: Record<OccurrenceStatus, "neutral" | "info" | "warning" | "good" | "outline"> = {
  Rascunho: "outline",
  Registrada: "info",
  EmAcompanhamento: "warning",
  Concluida: "good",
  Cancelada: "neutral",
};

export const notificationStatusLabels: Record<NotificationStatus, string> = {
  Pendente: "Na fila",
  Enviado: "Enviado",
  Falhou: "Falhou",
  Cancelado: "Cancelado",
};

export const notificationStatusTone: Record<NotificationStatus, "info" | "good" | "critical" | "neutral"> = {
  Pendente: "info",
  Enviado: "good",
  Falhou: "critical",
  Cancelado: "neutral",
};

export const revisionLabels: Record<string, string> = {
  registrada: "Registrada",
  editada: "Editada",
  em_acompanhamento: "Em acompanhamento",
  concluida: "Concluída",
  cancelada: "Cancelada",
};

export const formatDateTime = (iso: string) =>
  new Date(iso).toLocaleString("pt-BR", { timeZone: "America/Sao_Paulo", dateStyle: "short", timeStyle: "short" });

/** Valor para <input type="datetime-local"> no fuso de São Paulo. */
export function toLocalInput(iso: string) {
  const parts = new Intl.DateTimeFormat("sv-SE", {
    timeZone: "America/Sao_Paulo", year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit",
  }).format(new Date(iso));
  return parts.replace(" ", "T");
}

/** Converte o horário digitado (São Paulo, UTC−3 sem horário de verão) para ISO com fuso. */
export const fromLocalInput = (value: string) => `${value}:00-03:00`;

async function download(path: string) {
  const response = await fetch(path, { credentials: "same-origin" });
  if (!response.ok) {
    let title: string | undefined;
    try {
      title = ((await response.json()) as { title?: string }).title;
    } catch {
      // sem corpo
    }
    throw new ApiError(response.status, undefined, title ?? "Não foi possível baixar.");
  }
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(response.headers.get("Content-Disposition") ?? "")?.[1];
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = name ? decodeURIComponent(name) : "documento.pdf";
  a.hidden = true;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 30_000);
}

export const occurrencesApi = {
  categories: () => api.get<{ categories: Category[]; severities: Severity[] }>("/api/occurrences/categories"),
  createCategory: (name: string) => api.post<Category>("/api/occurrences/categories", { name }),
  updateCategory: (id: string, name: string, isActive: boolean) => api.put<Category>(`/api/occurrences/categories/${id}`, { name, isActive }),

  list: (filter: { studentId?: string; classroomId?: string; status?: OccurrenceStatus; from?: string; to?: string; page?: number }) => {
    const p = new URLSearchParams();
    for (const [k, v] of Object.entries(filter)) if (v !== undefined && v !== "") p.set(k, String(v));
    return api.get<Page<OccurrenceListItem>>(`/api/occurrences?${p}`);
  },
  get: (id: string) => api.get<OccurrenceDetails>(`/api/occurrences/${id}`),
  create: (input: OccurrenceInput, register: boolean) => api.post<{ id: string }>("/api/occurrences", { ...input, register }),
  update: (id: string, input: OccurrenceInput, reason: string | null, expectedVersion: number) =>
    api.put(`/api/occurrences/${id}`, { ...input, reason, expectedVersion }),
  register: (id: string, expectedVersion: number) => api.post(`/api/occurrences/${id}/register`, { expectedVersion }),
  changeStatus: (id: string, status: OccurrenceStatus, reason: string | null, expectedVersion: number) =>
    api.post(`/api/occurrences/${id}/status`, { status, reason, expectedVersion }),
  deleteDraft: (id: string) => api.delete(`/api/occurrences/${id}`),
  revision: (id: string, version: number) => api.get<Record<string, unknown>>(`/api/occurrences/${id}/revisions/${version}`),
  upload: (id: string, file: File) => {
    const body = new FormData();
    body.append("file", file);
    return apiFetch<AttachmentInfo>(`/api/occurrences/${id}/attachments`, { method: "POST", body });
  },
  downloadAttachment: (id: string, attachmentId: string) => download(`/api/occurrences/${id}/attachments/${attachmentId}`),
  removeAttachment: (id: string, attachmentId: string) => api.post(`/api/occurrences/${id}/attachments/${attachmentId}/remove`),
  ata: (id: string) => download(`/api/occurrences/${id}/ata`),
};

export const communicationsApi = {
  preview: (occurrenceId: string, templateId?: string) =>
    api.get<CommunicationPreview>(`/api/occurrences/${occurrenceId}/communications/preview${templateId ? `?templateId=${templateId}` : ""}`),
  authorize: (occurrenceId: string, body: { templateId: string; guardianIds: string[]; requestId: string; confirmDuplicate?: boolean }) =>
    api.post<NotificationView[]>(`/api/occurrences/${occurrenceId}/communications`, body),
  forOccurrence: (occurrenceId: string) => api.get<NotificationView[]>(`/api/occurrences/${occurrenceId}/communications`),
  list: (filter: { studentId?: string; status?: NotificationStatus; page?: number }) => {
    const p = new URLSearchParams();
    for (const [k, v] of Object.entries(filter)) if (v !== undefined && v !== "") p.set(k, String(v));
    return api.get<Page<NotificationView>>(`/api/communications?${p}`);
  },
  retry: (id: string) => api.post<NotificationView>(`/api/communications/${id}/retry`),
  cancel: (id: string) => api.post<NotificationView>(`/api/communications/${id}/cancel`),
  templates: () => api.get<Template[]>("/api/communications/templates"),
  updateTemplate: (id: string, body: { name: string; subject: string; body: string; isActive: boolean }) =>
    api.put<Template>(`/api/communications/templates/${id}`, body),
};

export const updateGuardianContact = (
  studentId: string,
  guardianId: string,
  body: { email: string | null; phone: string | null; isPrimaryContact: boolean; contactAuthorized: boolean; contactBasis: string | null },
) => api.put(`/api/students/${studentId}/guardians/${guardianId}/contact`, body);
