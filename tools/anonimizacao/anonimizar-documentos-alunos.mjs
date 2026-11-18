// Anonimiza a planilha real de documentos de alunos.
// - Dados pessoais → fictícios (nomes, CGM, nascimento, e-mail, endereço, responsável, CPF/RG, telefone).
// - Colunas de checklist → só valores de uma lista fechada; o resto vira "VERIFICAR".
// - Texto livre (observações, situação detalhada, colunas soltas) → removido ou reduzido a categoria.
// - O mapeamento real→fictício existe só em memória durante a execução e NÃO é salvo.
// Uso: node anonymize.mjs <entrada.xlsx> <saida.xlsx>
import crypto from "node:crypto";
import ExcelJS from "exceljs";

const [input, output] = process.argv.slice(2);
const rand = () => crypto.randomInt(0, 2 ** 31) / 2 ** 31;
const pick = (list) => list[Math.floor(rand() * list.length)];
const int = (min, max) => min + Math.floor(rand() * (max - min + 1));

const femaleNames = ["ANA", "BEATRIZ", "CAMILA", "CLARA", "DANIELA", "EDUARDA", "ELISA", "FERNANDA", "GABRIELA", "HELENA", "ISABELA", "JÚLIA", "LARISSA", "LETÍCIA", "LÍVIA", "LUANA", "MANUELA", "MARIANA", "MELISSA", "NATÁLIA", "OLÍVIA", "PRISCILA", "RAFAELA", "SARA", "SOFIA", "TALITA", "VALENTINA", "VITÓRIA", "YARA", "ALICE", "BIANCA", "CECÍLIA", "DÉBORA", "ESTER", "FLÁVIA", "GIOVANA", "HELOÍSA", "IASMIN", "JOANA", "LAURA"];
const maleNames = ["ARTHUR", "BERNARDO", "CAIO", "DANILO", "ENZO", "FELIPE", "GUSTAVO", "HEITOR", "IGOR", "JOAQUIM", "LEONARDO", "LORENZO", "LUCAS", "MATEUS", "MURILO", "NICOLAS", "OTÁVIO", "PEDRO", "RAFAEL", "RENATO", "SAMUEL", "THIAGO", "VINÍCIUS", "BRUNO", "CARLOS", "DIEGO", "EMANUEL", "FÁBIO", "GABRIEL", "HENRIQUE", "ÍCARO", "JOÃO", "KAUÃ", "LEVI", "MIGUEL", "NATAN", "OTÁVIO", "PAULO", "RODRIGO", "TOMÁS"];
const surnames = ["ALMEIDA", "ARAÚJO", "BARBOSA", "BATISTA", "CAMPOS", "CARDOSO", "CARVALHO", "CASTRO", "CORREIA", "COSTA", "DIAS", "DUARTE", "FARIAS", "FERREIRA", "FONSECA", "FREITAS", "GOMES", "LIMA", "LOPES", "MACHADO", "MARTINS", "MELO", "MENDES", "MONTEIRO", "MOREIRA", "NASCIMENTO", "NUNES", "OLIVEIRA", "PEREIRA", "PINTO", "RAMOS", "REIS", "RIBEIRO", "ROCHA", "RODRIGUES", "SANTOS", "SILVA", "SOARES", "SOUZA", "TEIXEIRA", "VIANA", "VIEIRA"];
const streets = ["RUA DAS ACÁCIAS", "RUA DOS IPÊS", "RUA DAS PALMEIRAS", "AVENIDA DAS ARAUCÁRIAS", "RUA DOS GIRASSÓIS", "RUA DAS ORQUÍDEAS", "RUA DO CEDRO", "RUA DAS CANELEIRAS", "AVENIDA DOS PINHEIROS", "RUA DAS MARGARIDAS", "RUA DO JACARANDÁ", "RUA DAS HORTÊNSIAS", "RUA DOS LÍRIOS", "TRAVESSA DAS VIOLETAS", "RUA DA AROEIRA"];
const districts = ["JARDIM EXEMPLO", "VILA FICTÍCIA", "BAIRRO MODELO", "PARQUE DAS FLORES", "JARDIM DAS ÁGUAS", "VILA NOVA ESPERANÇA", "CONJUNTO AMOSTRA", "JARDIM PRIMAVERA"];

// Valores reais (coletados ANTES de gerar qualquer dado): nenhum valor fictício pode contê-los.
const secrets = new Set();
const isSafe = (candidate) => { const s = candidate.toUpperCase(); for (const secret of secrets) if (s.includes(secret)) return false; return true; };
const safely = (generate) => { for (;;) { const v = generate(); if (isSafe(v)) return v; } };

const usedNames = new Set();
function fakeName(words, gender) {
  for (;;) {
    const given = gender === "F" ? femaleNames : gender === "M" ? maleNames : rand() < 0.5 ? femaleNames : maleNames;
    const parts = [pick(given)];
    if (words >= 4 && rand() < 0.6) parts.push(pick(given));
    parts.push(pick(surnames));
    if (words >= 3) parts.push(pick(surnames));
    const name = [...new Set(parts)].join(" ");
    if (!usedNames.has(name) && isSafe(name)) { usedNames.add(name); return name; }
  }
}
const slug = (s) => s.normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase();

// CPF com dígitos verificadores propositalmente INVÁLIDOS: nunca coincide com um CPF real.
function fakeCpf() {
  const base = Array.from({ length: 9 }, () => int(0, 9));
  const dv = (digits) => { const s = digits.reduce((acc, d, i) => acc + d * (digits.length + 1 - i), 0); const r = (s * 10) % 11; return r === 10 ? 0 : r; };
  const d1 = dv(base), d2 = dv([...base, d1]);
  const all = [...base, (d1 + 1) % 10, (d2 + 3) % 10].join("");
  return `${all.slice(0, 3)}.${all.slice(3, 6)}.${all.slice(6, 9)}-${all.slice(9)}`;
}
const fakePhone = () => `(00) 9${int(1000, 9999)}-${int(1000, 9999)}`;

const cgmMap = new Map();
const usedCgm = new Set();
function fakeCgm(original) {
  const key = String(original).trim();
  if (!cgmMap.has(key)) {
    let v; do { v = String(int(1_000_000_000, 1_999_999_999)); } while (usedCgm.has(v));
    usedCgm.add(v); cgmMap.set(key, v);
  }
  return Number(cgmMap.get(key));
}
const studentByCgm = new Map(); // CGM real → nome fictício
const classCodeMap = new Map();
const fakeClassCode = (code) => { if (!classCodeMap.has(code)) classCodeMap.set(code, String(9_000_000 + classCodeMap.size + 1)); return classCodeMap.get(code); };

const text = (v) => (v && typeof v === "object" ? (v instanceof Date ? v : v.richText ? v.richText.map((t) => t.text).join("") : v.text ?? v.result ?? null) : v);
const norm = (v) => String(v ?? "").trim().replace(/\s+/g, " ").toUpperCase();
const shiftDate = (d) => {
  const copy = new Date(d.getTime() + int(-90, 90) * 86_400_000);
  return copy.getUTCFullYear() === d.getUTCFullYear() ? copy : d.getUTCMonth() < 6 ? new Date(Date.UTC(d.getUTCFullYear(), 0, int(2, 28))) : new Date(Date.UTC(d.getUTCFullYear(), 11, int(2, 28)));
};

const CHECKLIST = new Set(["OK", "0K", "FALTA", "NÃO AUTORIZADO", "NÃQ AUTORIZADO", "CÓPIA", "ESTRANGEIRO", "RNM ESTRANGEIRA", "RNM", "NÃO TEM", "FAZER", "SEM CONTATO"]);
const RELATION = new Set(["MÃE", "PAI", "AVÓS", "AVÓ", "AVÔ", "OUTROS", "TERMO", "PRÓPRIO ALUNO"]);
function checklist(v) {
  if (v instanceof Date) return shiftDate(v);
  const n = norm(v);
  if (!n) return null;
  if (n === "0K") return "OK";
  if (n === "NÃQ AUTORIZADO") return "NÃO AUTORIZADO";
  return CHECKLIST.has(n) ? n : "VERIFICAR";
}

const src = new ExcelJS.Workbook();
await src.xlsx.readFile(input);
const out = new ExcelJS.Workbook();
out.creator = "Sinedu — amostra anonimizada";

const remember = (v) => { const s = v instanceof Date ? null : String(v ?? "").trim(); if (s && s.length >= 5) secrets.add(s.toUpperCase()); };
{
  const o = src.getWorksheet("Original");
  for (let r = 2; r <= o.rowCount; r++) for (const c of [2, 3, 6, 7, 8, 9, 25]) remember(text(o.getRow(r).getCell(c).value));
  for (const ws of src.worksheets.filter((w) => w.name.toLowerCase().startsWith("frequ"))) {
    for (let r = 2; r <= ws.rowCount; r++) {
      remember(text(ws.getRow(r).getCell(1).value));
      remember(text(ws.getRow(r).getCell(2).value));
      for (const code of String(text(ws.getRow(r).getCell(3).value) ?? "").match(/d{6,}/g) ?? []) remember(code);
    }
  }
}

// ---- Aba Original ----
const original = src.getWorksheet("Original");
const sheet = out.addWorksheet("Original");
const KEEP_COLS = 26;
const header = original.getRow(1);
sheet.addRow(Array.from({ length: KEEP_COLS }, (_, i) => text(header.getCell(i + 1).value) ?? null));
let students = 0;

for (let r = 2; r <= original.rowCount; r++) {
  const row = original.getRow(r);
  const cgm = text(row.getCell(2).value);
  if (!cgm) continue;
  students++;
  const v = (c) => text(row.getCell(c).value);
  [2, 3, 6, 7, 8, 9, 25].forEach((c) => remember(v(c)));

  const studentWords = String(v(3) ?? "").trim().split(/\s+/).length;
  const studentName = fakeName(Math.max(3, studentWords));
  studentByCgm.set(String(cgm).trim(), studentName);
  const [first, ...rest] = studentName.split(" ");
  const birth = v(5) instanceof Date ? shiftDate(v(5)) : null;
  const relation = norm(v(11));
  const frequency = v(23);
  const freqNumber = typeof frequency === "number" ? frequency : Number.parseFloat(String(frequency ?? "").replace(",", ".").replace("%", ""));
  const situation = norm(v(24)).split(" ")[0];

  sheet.addRow([
    v(1),                                                      // nº de chamada
    fakeCgm(cgm),                                              // CGM fictício
    studentName,
    v(4),                                                      // turma (mantida)
    birth,
    v(6) ? safely(() => `${slug(first)}.${slug(rest.at(-1))}${int(10, 99)}@aluno.exemplo.edu.br`) : null,
    v(7) ? safely(() => `${pick(streets)}, ${int(10, 2999)} - ${pick(districts)}`) : null,
    // Nome do responsável coerente com o vínculo (mãe/avó → feminino; pai/avô → masculino).
    v(8) ? fakeName(String(v(8)).trim().split(/\s+/).length, ["MÃE", "AVÓ"].includes(relation) ? "F" : ["PAI", "AVÔ"].includes(relation) ? "M" : undefined) : null,
    v(9) ? safely(fakeCpf) : null,
    checklist(v(10)),
    relation ? (RELATION.has(relation) ? relation : "OUTROS") : null,
    checklist(v(12)),
    checklist(v(13)),
    checklist(v(14)),
    checklist(v(15)),                                          // "RG ALUNO" (checklist)
    checklist(v(16)),
    checklist(v(17)),
    checklist(v(18)),
    checklist(v(19)),
    norm(v(20)) === "OK" ? "OK" : null,                        // ficha de saúde: só a entrega, nunca conteúdo
    checklist(v(21)) === "VERIFICAR" ? "OUTRA ESCOLA" : checklist(v(21)),
    checklist(v(22)),
    Number.isFinite(freqNumber) ? Math.round(Math.min(Math.max(freqNumber * (freqNumber > 1 ? 1 : 100) + int(-3, 3), 0), 100)) / (freqNumber > 1 ? 1 : 100) : null,
    ["SF", "SFD"].includes(situation) ? situation : null,      // situação: só a sigla
    v(25) ? safely(fakePhone) : null,
    null,                                                      // observações: removidas
  ]);
}
sheet.getColumn(5).numFmt = "dd/mm/yyyy";

// ---- Abas de frequência ----
let freqRows = 0;
for (const ws of src.worksheets.filter((w) => w.name.toLowerCase().startsWith("frequ"))) {
  const target = out.addWorksheet(ws.name);
  target.addRow([1, 2, 3, 4].map((c) => text(ws.getRow(1).getCell(c).value)));
  for (let r = 2; r <= ws.rowCount; r++) {
    const row = ws.getRow(r);
    const cgm = text(row.getCell(2).value);
    if (!cgm) continue;
    freqRows++;
    remember(text(row.getCell(1).value));
    remember(cgm);
    const key = String(cgm).trim();
    if (!studentByCgm.has(key)) studentByCgm.set(key, fakeName(3));
    const turma = String(text(row.getCell(3).value) ?? "").replace(/\d{6,}/g, (code) => fakeClassCode(code));
    for (const code of String(text(row.getCell(3).value) ?? "").match(/\d{6,}/g) ?? []) remember(code);
    const presence = text(row.getCell(4).value);
    const presenceNumber = typeof presence === "number" ? presence : Number.parseFloat(String(presence ?? "").replace(",", ".").replace("%", ""));
    target.addRow([studentByCgm.get(key), fakeCgm(cgm), turma, Number.isFinite(presenceNumber) ? presenceNumber : null]);
  }
}

await out.xlsx.writeFile(output);

// ---- Verificação: nenhum valor real pode aparecer no arquivo gerado ----
const check = new ExcelJS.Workbook();
await check.xlsx.readFile(output);
let leaks = 0, cells = 0;
for (const ws of check.worksheets) {
  ws.eachRow((row, r) => row.eachCell((cell, c) => {
    cells++;
    const value = text(cell.value);
    if (value instanceof Date || value === null || value === undefined) return;
    const s = String(value).toUpperCase();
    if (typeof value === "number") { if (secrets.has(s)) { leaks++; console.log(`  vazamento numérico: aba "${ws.name}" linha ${r} col ${c}`); } return; }
    for (const secret of secrets) if (s.includes(secret)) { leaks++; console.log(`  vazamento: aba "${ws.name}" linha ${r} col ${c}; trecho de ${secret.length} caracteres; célula inteira igual: ${s === secret}; só dígitos: ${/^d+$/.test(secret)}`); break; }
  }));
}
console.log(`alunos: ${students} · linhas de frequência: ${freqRows} · abas: ${check.worksheets.length} · células verificadas: ${cells}`);
console.log(`valores reais conferidos: ${secrets.size} · vazamentos encontrados: ${leaks}`);
if (leaks > 0) process.exitCode = 1;
