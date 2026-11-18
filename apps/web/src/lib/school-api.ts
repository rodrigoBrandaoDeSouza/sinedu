import { api } from "@sinedu/ui";

export type Shift = "Manha" | "Tarde" | "Noite" | "Integral";
export type Stage = "Fundamental" | "Medio" | "Outra";
export type EnrollmentStatus = "Ativa" | "Transferida" | "Remanejada" | "Concluida" | "Cancelada";
export type Relationship = "Mae" | "Pai" | "Avos" | "Responsavel" | "ProprioAluno" | "Outro";
export type StaffFunction = "Diretor" | "Pedagogo" | "Professor" | "Secretaria" | "Outro";

export interface Term {
  id: string;
  number: number;
  name: string;
  startsOn: string;
  endsOn: string;
}

export interface SchoolYear {
  id: string;
  year: number;
  startsOn: string;
  endsOn: string;
  status: "Planned" | "Active" | "Closed";
  terms: Term[];
}

export interface Teacher {
  assignmentId: string;
  staffMemberId: string;
  name: string;
  subject: string | null;
}

export interface Classroom {
  id: string;
  name: string;
  gradeId: string;
  gradeName: string;
  stage: Stage;
  section: string;
  shift: Shift;
  status: "Active" | "Closed";
  capacity: number | null;
  externalCode: string | null;
  schoolYearId: string;
  teachers: Teacher[];
}

export interface StudentRow {
  id: string;
  name: string;
  civilName: string | null;
  institutionalCode: string | null;
  age: number | null;
  classroom: { id: string; name: string } | null;
  callNumber: number | null;
  status: "Active" | "Inactive";
}

export interface Page<T> {
  total: number;
  page: number;
  pageSize: number;
  items: T[];
}

export interface Guardian {
  id: string;
  name: string;
  relationship: Relationship;
  isPrimaryContact: boolean;
  contactAuthorized: boolean;
  phone: string | null;
  email: string | null;
  document?: string;
  contactBasis?: string;
}

export interface StudentDetails {
  id: string;
  fullName: string;
  socialName: string | null;
  birthDate: string | null;
  age: number | null;
  institutionalCode: string | null;
  institutionalEmail: string | null;
  status: "Active" | "Inactive";
  classroom: { id: string; name: string } | null;
  enrollments: {
    id: string;
    classroomId: string;
    classroom: string;
    year: number | null;
    status: EnrollmentStatus;
    startsOn: string;
    endsOn: string | null;
    callNumber: number | null;
  }[];
  /** Ausente para quem não pode ver responsáveis (Consulta). */
  guardians?: Guardian[];
  /** Ausente para quem não pode ver endereço. */
  address?: string;
  canEdit: boolean;
}

export interface StaffMember {
  id: string;
  fullName: string;
  function: StaffFunction;
  professionalEmail: string | null;
  professionalPhone: string | null;
  hasAccount: boolean;
}

export const shiftLabels: Record<Shift, string> = { Manha: "Manhã", Tarde: "Tarde", Noite: "Noite", Integral: "Integral" };
export const stageLabels: Record<Stage, string> = { Fundamental: "Ensino Fundamental", Medio: "Ensino Médio", Outra: "Outras etapas" };
export const relationshipLabels: Record<Relationship, string> = {
  Mae: "Mãe",
  Pai: "Pai",
  Avos: "Avós",
  Responsavel: "Responsável legal",
  ProprioAluno: "Próprio aluno",
  Outro: "Outro",
};
export const enrollmentLabels: Record<EnrollmentStatus, string> = {
  Ativa: "Ativa",
  Transferida: "Transferida",
  Remanejada: "Remanejada",
  Concluida: "Concluída",
  Cancelada: "Cancelada",
};
export const staffFunctionLabels: Record<StaffFunction, string> = {
  Diretor: "Direção",
  Pedagogo: "Pedagogia",
  Professor: "Professor(a)",
  Secretaria: "Secretaria",
  Outro: "Outro",
};

export const schoolApi = {
  years: () => api.get<SchoolYear[]>("/api/school/years"),
  classrooms: (yearId?: string) => api.get<Classroom[]>(`/api/school/classrooms${yearId ? `?yearId=${yearId}` : ""}`),
  classroom: (id: string) => api.get<Classroom>(`/api/school/classrooms/${id}`),
  classroomCounts: () => api.get<{ classroomId: string; students: number }[]>("/api/students/classroom-counts"),
  students: (params: { query?: string; classroomId?: string; page?: number; pageSize?: number }) => {
    const search = new URLSearchParams();
    if (params.query) search.set("query", params.query);
    if (params.classroomId) search.set("classroomId", params.classroomId);
    search.set("page", String(params.page ?? 1));
    search.set("pageSize", String(params.pageSize ?? 25));
    return api.get<Page<StudentRow>>(`/api/students?${search}`);
  },
  student: (id: string) => api.get<StudentDetails>(`/api/students/${id}`),
  staff: () => api.get<StaffMember[]>("/api/school/staff"),
};

export function initials(name: string) {
  return name
    .split(" ")
    .filter((w) => w.length > 2)
    .slice(0, 2)
    .map((w) => w[0])
    .join("");
}

export function formatDate(value: string | null | undefined) {
  if (!value) return "—";
  const [y, m, d] = value.split("-");
  return `${d}/${m}/${y}`;
}
