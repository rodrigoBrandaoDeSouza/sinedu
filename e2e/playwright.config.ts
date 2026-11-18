import { defineConfig } from "@playwright/test";

/**
 * E2E contra o ambiente local completo (infra + APIs + fronts rodando, `seed-dev` aplicado e a amostra importada na escola-demo).
 * Os subdomínios *.localhost são resolvidos para 127.0.0.1 pelo próprio Chromium.
 * Rodar na raiz: npm run e2e
 */
export default defineConfig({
  testDir: ".",
  testMatch: "*.spec.ts",
  timeout: 120_000,
  workers: 1,
  reporter: [["list"], ["html", { open: "never", outputFolder: "../playwright-report" }]],
  outputDir: "../test-results",
  use: {
    viewport: { width: 1440, height: 900 },
    launchOptions: { args: ["--host-resolver-rules=MAP *.localhost 127.0.0.1"] },
    acceptDownloads: true,
    trace: "retain-on-failure",
  },
});
