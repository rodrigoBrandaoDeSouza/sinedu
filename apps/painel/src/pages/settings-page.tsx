import { AppearanceSettings, Badge, Card, CardBody, CardHeader, PageHeader } from "@sinedu/ui";

export function SettingsPage() {
  return (
    <>
      <PageHeader title="Configurações" description="Preferências pessoais e parâmetros da plataforma." />
      <div className="grid max-w-3xl gap-4">
        <Card>
          <CardHeader title="Aparência" description="Vale para este navegador. Em outro computador ou celular, escolha de novo." />
          <CardBody>
            <AppearanceSettings />
          </CardBody>
        </Card>
        <Card>
          <CardHeader
            title="Parâmetros da plataforma"
            description="Planos, subdomínios reservados e demais parâmetros."
            actions={<Badge tone="neutral">Em breve · P-06</Badge>}
          />
          <CardBody />
        </Card>
      </div>
    </>
  );
}
