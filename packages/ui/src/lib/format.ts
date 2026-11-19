const integer = new Intl.NumberFormat("pt-BR", { maximumFractionDigits: 0 });
const decimal = new Intl.NumberFormat("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 });
const compact = new Intl.NumberFormat("pt-BR", { notation: "compact", maximumFractionDigits: 1 });
const currency = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
const money = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const currencyCompact = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
  notation: "compact",
  maximumFractionDigits: 1,
});
const shortDate = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "short", timeZone: "America/Sao_Paulo" });

export const format = {
  /** 1.284 */
  integer: (value: number) => integer.format(value),
  /** 12,4 */
  decimal: (value: number) => decimal.format(value),
  /** Abaixo de 10.000 por extenso, acima compacto (12,9 mil). */
  auto: (value: number) => (Math.abs(value) < 10_000 ? integer.format(value) : compact.format(value)),
  /** 0.734 → "73%" */
  percent: (ratio: number, digits = 0) =>
    `${(ratio * 100).toLocaleString("pt-BR", { maximumFractionDigits: digits, minimumFractionDigits: digits })}%`,
  currency: (value: number) => currency.format(value),
  /** R$ 1.234,50 (faturas: sempre com centavos) */
  money: (value: number) => money.format(value),
  currencyCompact: (value: number) => currencyCompact.format(value),
  shortDate: (iso: string) => shortDate.format(new Date(iso)).replace(".", ""),
};
