import { AppearanceSettings, Card, CardBody, CardHeader, PageHeader } from "@sinedu/ui";

/** Preferências pessoais. Ficam neste navegador (não vão para o servidor). */
export function SettingsPage() {
  return (
    <>
      <PageHeader title="Configurações" description="Preferências pessoais de uso do sistema." />
      <Card className="max-w-3xl">
        <CardHeader title="Aparência" description="Vale para este navegador. Em outro computador ou celular, escolha de novo." />
        <CardBody>
          <AppearanceSettings />
        </CardBody>
      </Card>
    </>
  );
}
