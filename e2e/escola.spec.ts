import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import { signIn, urls } from "./helpers";

test.describe("Login único e isolamento entre escolas", () => {
  test("Diretora entra na própria escola e vê os indicadores reais", async ({ page }) => {
    await signIn(page, urls.demo, "diretora@escola-demo.local");
    await expect(page.getByText("Alunos matriculados").first()).toBeVisible();
    await expect(page.getByText("Comparação entre turmas").first()).toBeVisible();
    await expect(page.getByText(/Dados fictícios/)).toHaveCount(0);
  });

  test("A mesma conta não entra em outra escola", async ({ page }) => {
    await signIn(page, urls.demo, "diretora@escola-demo.local");
    await page.goto(urls.modelo);
    await expect(page).toHaveURL(/sem-acesso/);
  });

  test("Sair encerra a sessão e volta para o login único", async ({ page }) => {
    await signIn(page, urls.demo, "diretora@escola-demo.local");
    await page.getByRole("button", { name: "Sair" }).click();
    await expect(page).toHaveURL(/conta\.localhost:5175\/entrar/);
    await page.goto(urls.demo);
    await expect(page).toHaveURL(/entrar/); // a sessão da escola não vale mais
  });

  test("Sem acesso à escola, 'Entrar com outra conta' leva ao login", async ({ page }) => {
    await signIn(page, urls.demo, "diretora@escola-demo.local");
    await page.goto(urls.modelo);
    await expect(page).toHaveURL(/sem-acesso/);
    await page.getByRole("button", { name: /entrar com outra conta/i }).click();
    await expect(page).toHaveURL(/conta\.localhost:5175\/entrar/);
  });

  test("A sessão de uma escola não autentica a API de outra", async ({ page, request }) => {
    await signIn(page, urls.demo, "diretora@escola-demo.local");
    const cookie = (await page.context().cookies(urls.demo)).map((c) => `${c.name}=${c.value}`).join("; ");
    // O Node não resolve *.localhost: chama o mesmo servidor por 127.0.0.1 com o Host da outra escola.
    const response = await request.get("http://127.0.0.1:5173/api/me", { headers: { Host: new URL(urls.modelo).host, Cookie: cookie } });
    expect(response.status()).toBe(401);
  });
});

test.describe("Fluxos da escola", () => {
  test("Ocorrência: registrar, comunicar, gerar ata e concluir", async ({ page }) => {
    await signIn(page, `${urls.demo}/alunos`, "diretora@escola-demo.local");
    await page.locator("tbody tr").nth(5).click();
    await page.waitForURL(/alunos\/.+/);

    const email = `e2e-${Date.now()}@exemplo.test`;
    await page.getByRole("button", { name: /contato/i }).first().click();
    await page.getByLabel("E-mail").fill(email);
    await page.getByLabel("Autoriza receber comunicações da escola").check();
    await page.getByLabel("Fundamento da autorização").fill("Termo assinado na matrícula (E2E)");
    await page.getByRole("button", { name: /^salvar$/i }).click();
    await expect(page.getByText("Contato autorizado").first()).toBeVisible();

    await page.getByRole("button", { name: /nova ocorrência/i }).click();
    const marker = `E2E-${Date.now()}`;
    await page.getByLabel("Descrição do fato").fill(`${marker} Discussão em sala, com interrupção da atividade.`);
    await page.getByRole("button", { name: /registrar ocorrência/i }).click();
    await expect(page.getByText("v1 · Registrada")).toBeVisible();

    await page.getByRole("button", { name: /^comunicar$/i }).click();
    await page.getByRole("button", { name: /autorizar e enviar/i }).click();
    await expect(page.getByText("Enviado").first()).toBeVisible();

    // O e-mail chegou ao Mailpit e não carrega o conteúdo do fato.
    const search = await (await fetch(`${urls.mailpit}/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`)).json();
    expect(search.messages_count).toBe(1);
    const message = await (await fetch(`${urls.mailpit}/api/v1/message/${search.messages[0].ID}`)).json();
    expect(message.Text).not.toContain(marker);

    const [ata] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: /ata \(pdf\)/i }).click()]);
    expect(ata.suggestedFilename()).toMatch(/^ata-ocorrencia-.+\.pdf$/);

    await page.getByRole("button", { name: /^concluir$/i }).click();
    await page.getByLabel("Conclusão").fill("Família compareceu (E2E).");
    await page.getByRole("button", { name: /^confirmar$/i }).click();
    await expect(page.getByText("v2 · Concluída")).toBeVisible();
  });

  test("Saúde: com autenticação recente, lê e grava ficha versionada", async ({ page }) => {
    await signIn(page, `${urls.demo}/alunos`, "diretora@escola-demo.local");
    await page.locator("tbody tr").nth(6).click();
    await page.getByRole("tab", { name: /saúde/i }).click();
    await page.getByRole("button", { name: /^(Criar ficha|Editar)$/ }).first().click();
    await page.getByLabel("Alergias e cuidados").fill(`Alergia fictícia ${Date.now()}`);
    await page.getByLabel("Motivo da alteração").fill("Atualização de teste E2E");
    await page.getByRole("button", { name: /salvar versão/i }).click();
    await expect(page.getByText(/Alergia fictícia/).first()).toBeVisible();
  });

  test("Exportação de indicadores sai só com agregados", async ({ page }) => {
    await signIn(page, urls.demo, "diretora@escola-demo.local");
    await page.getByRole("button", { name: /^exportar$/i }).click();
    const [csv] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: /planilha \(csv\)/i }).click()]);
    const text = await readFile((await csv.path())!, "utf8");
    expect(text).toContain("Ocorrências registradas");
    expect(text).not.toMatch(/E2E-\d+/);
  });
});

test.describe("Painel da plataforma", () => {
  test("Super admin vê as escolas e nenhum dado de aluno", async ({ page }) => {
    await signIn(page, urls.painel, "admin@sinedu.local");
    await expect(page.getByText(/Colégio Estadual Demonstração/).first()).toBeVisible();
    await expect(page.locator("main")).not.toContainText(/CGM|alergia/i);
  });
});
