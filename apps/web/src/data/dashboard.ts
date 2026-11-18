/**
 * Dados do dashboard bimestral.
 *
 * ⚠️ DADOS FICTÍCIOS — a API de indicadores entra no item F6-01 do backlog.
 * Nunca usar dados reais de alunos em mocks, testes ou demonstrações.
 */

export interface DashboardFilters {
  year: string;
  term: string;
  classroom: string;
  category: string;
}

export interface DashboardData {
  termLabel: string;
  previousTermLabel: string;
  enrolled: number;
  enrolledDelta: number;
  occurrences: number;
  occurrencesPrevious: number;
  studentsWithOccurrence: number;
  studentsWithOccurrencePrevious: number;
  status: { registered: number; followUp: number; concluded: number };
  notifications: { sent: number; pending: number; failed: number };
  weekly: { week: string; current: number | null; previous: number }[];
  byCategory: { label: string; value: number }[];
  byClassroom: { classroom: string; shift: string; enrolled: number; occurrences: number }[];
  followUps: {
    id: string;
    student: string;
    classroom: string;
    category: string;
    occurredAt: string;
    status: "registrada" | "em_acompanhamento";
    owner: string;
  }[];
}

const classrooms = [
  { classroom: "6º ano A", shift: "Manhã", enrolled: 32 },
  { classroom: "6º ano B", shift: "Tarde", enrolled: 29 },
  { classroom: "7º ano A", shift: "Manhã", enrolled: 31 },
  { classroom: "7º ano B", shift: "Tarde", enrolled: 27 },
  { classroom: "8º ano A", shift: "Manhã", enrolled: 30 },
  { classroom: "8º ano B", shift: "Tarde", enrolled: 33 },
  { classroom: "9º ano A", shift: "Manhã", enrolled: 28 },
  { classroom: "1ª série EM", shift: "Noite", enrolled: 35 },
];

/** Gerador determinístico para variar os números por bimestre sem aleatoriedade entre renders. */
function seeded(seed: number) {
  let s = seed;
  return () => {
    s = (s * 9301 + 49297) % 233280;
    return s / 233280;
  };
}

export async function fetchDashboard(filters: DashboardFilters): Promise<DashboardData> {
  await new Promise((r) => setTimeout(r, 350));

  const termNumber = Number(filters.term);
  const rand = seeded(termNumber * 97 + Number(filters.year));
  const scale = 0.8 + termNumber * 0.08;

  const byClassroom = classrooms.map((c) => ({ ...c, occurrences: Math.round((2 + rand() * 9) * scale) }));
  const occurrences = byClassroom.reduce((s, c) => s + c.occurrences, 0);
  const weeks = 10;
  const currentWeeks = termNumber === 4 ? 7 : weeks;
  const weekly = Array.from({ length: weeks }, (_, i) => ({
    week: `Sem ${i + 1}`,
    current: i < currentWeeks ? Math.round(3 + rand() * 9 * scale) : null,
    previous: Math.round(3 + rand() * 9),
  }));

  const categoryWeights: [string, number][] = [
    ["Indisciplina", 0.31],
    ["Conflito entre alunos", 0.22],
    ["Atraso recorrente", 0.18],
    ["Desrespeito", 0.13],
    ["Outros", 0.1],
    ["Dano material", 0.06],
  ];

  const concluded = Math.round(occurrences * 0.58);
  const followUp = Math.round(occurrences * 0.27);

  return {
    termLabel: `${termNumber}º bimestre`,
    previousTermLabel: termNumber > 1 ? `${termNumber - 1}º bimestre` : "4º bimestre de " + (Number(filters.year) - 1),
    enrolled: 612,
    enrolledDelta: termNumber === 1 ? 0 : -3 + Math.round(rand() * 8),
    occurrences,
    occurrencesPrevious: Math.round(occurrences * (0.85 + rand() * 0.3)),
    studentsWithOccurrence: Math.round(occurrences * 0.64),
    studentsWithOccurrencePrevious: Math.round(occurrences * 0.6),
    status: { concluded, followUp, registered: occurrences - concluded - followUp },
    notifications: { sent: Math.round(occurrences * 0.47), pending: 3, failed: 2 },
    weekly,
    byCategory: categoryWeights
      .map(([label, w]) => ({ label, value: Math.max(1, Math.round(occurrences * w)) }))
      .sort((a, b) => b.value - a.value),
    byClassroom,
    followUps: [
      { id: "1", student: "Aluno Fictício 014", classroom: "8º ano B", category: "Conflito entre alunos", occurredAt: "2026-11-17", status: "em_acompanhamento", owner: "Pedagogia" },
      { id: "2", student: "Aluna Fictícia 203", classroom: "6º ano A", category: "Atraso recorrente", occurredAt: "2026-11-14", status: "registrada", owner: "Direção" },
      { id: "3", student: "Aluno Fictício 087", classroom: "1ª série EM", category: "Indisciplina", occurredAt: "2026-11-13", status: "em_acompanhamento", owner: "Pedagogia" },
      { id: "4", student: "Aluna Fictícia 152", classroom: "7º ano B", category: "Desrespeito", occurredAt: "2026-11-11", status: "registrada", owner: "Pedagogia" },
      { id: "5", student: "Aluno Fictício 331", classroom: "9º ano A", category: "Dano material", occurredAt: "2026-11-10", status: "em_acompanhamento", owner: "Direção" },
    ],
  };
}
