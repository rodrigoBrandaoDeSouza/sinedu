import type { RouteObject } from "react-router";
import { PanelLayout } from "./layout/panel-layout";
import { ComingSoonPage } from "./pages/coming-soon-page";
import { AccountsPage } from "./pages/accounts-page";
import { EmailTestPage } from "./pages/email-test-page";
import { OverviewPage } from "./pages/overview-page";
import { SettingsPage } from "./pages/settings-page";
import { SupportPage } from "./pages/support-page";
import { TenantsPage } from "./pages/tenants-page";
import { NetworksPage } from "./pages/networks-page";
import { ContractsPage } from "./pages/contracts-page";
import { InvoicesPage } from "./pages/invoices-page";

const soon = (path: string, title: string, description: string, backlog: string): RouteObject => ({
  path,
  element: <ComingSoonPage title={title} description={description} backlog={backlog} />,
});

export const routes: RouteObject[] = [
  {
    element: <PanelLayout />,
    children: [
      { index: true, element: <OverviewPage /> },
      { path: "escolas", element: <TenantsPage /> },
      { path: "redes", element: <NetworksPage /> },
      { path: "suporte", element: <SupportPage /> },
      { path: "contratos", element: <ContractsPage /> },
      { path: "faturas", element: <InvoicesPage /> },
      soon("auditoria", "Auditoria", "Ações realizadas no painel da plataforma.", "F1-09"),
      { path: "contas", element: <AccountsPage /> },
      { path: "teste-email", element: <EmailTestPage /> },
      { path: "configuracoes", element: <SettingsPage /> },
      soon("*", "Página não encontrada", "O endereço acessado não existe.", "—"),
    ],
  },
];
