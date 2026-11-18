import { expect, type Page } from "@playwright/test";
import crypto from "node:crypto";
import { readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

/** Credenciais de DESENVOLVIMENTO (README): nunca valem fora do ambiente local. */
export const PASSWORD = "Sinedu-Dev-2026!";
const TOTP_SECRET = "KRSXG5CTMVRXEZLUKNSWG4TFOQ2DAMZR";

export const urls = {
  demo: "http://escola-demo.localhost:5173",
  modelo: "http://escola-modelo.localhost:5173",
  painel: "http://painel.localhost:5174",
  mailpit: "http://localhost:8025",
};

function base32(s: string) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = "";
  for (const c of s) bits += alphabet.indexOf(c).toString(2).padStart(5, "0");
  return Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
}

// O worker do Playwright reinicia depois de uma falha: o último passo usado fica num arquivo, não na memória.
const stepFile = join(tmpdir(), "sinedu-e2e-totp-step");
const readStep = () => {
  try {
    return Number(readFileSync(stepFile, "utf8")) || 0;
  } catch {
    return 0;
  }
};

/**
 * Código TOTP para o segredo das contas de dev. O servidor recusa reuso do mesmo código (proteção contra replay),
 * então, se a janela de 30 s atual já foi usada por um teste anterior, espera a próxima.
 */
export async function totp() {
  while (Math.floor(Date.now() / 30_000) <= readStep()) {
    await new Promise((r) => setTimeout(r, 1_000));
  }
  const step = Math.floor(Date.now() / 30_000);
  writeFileSync(stepFile, String(step));
  const counter = Buffer.alloc(8);
  counter.writeBigInt64BE(BigInt(step));
  const h = crypto.createHmac("sha1", base32(TOTP_SECRET)).update(counter).digest();
  const o = h[h.length - 1]! & 15;
  return String((h.readUInt32BE(o) & 0x7fffffff) % 1e6).padStart(6, "0");
}

/** Login único: abre a área protegida, autentica em conta. com senha + TOTP e volta. */
export async function signIn(page: Page, url: string, email: string) {
  await page.goto(url);
  await page.waitForURL(/entrar/);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(PASSWORD);
  await page.getByRole("button", { name: /entrar/i }).click();
  await page.getByLabel("Código").fill(await totp());
  await page.getByRole("button", { name: /verificar/i }).click();
  // O primeiro login depois de subir as APIs pode passar de 5 s (aquecimento).
  await expect(page).toHaveURL((u) => u.host === new URL(url).host, { timeout: 30_000 });
}
