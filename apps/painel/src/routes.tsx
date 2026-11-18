import type { RouteObject } from "react-router";
import { PanelLayout } from "./layout/panel-layout";
import { ComingSoonPage } from "./pages/coming-soon-page";
import { AccountsPage } from "./pages/accounts-page";
import { EmailTestPage } from "./pages/email-test-page";
import { OverviewPage } from "./pages/overview-page";
import { TenantsPage } from "./pages/tenants-page";

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
      soon("redes", "Redes", "Agrupamento de escolas por rede estadual ou municipal.", "P-04"),
      soon("suporte", "Acessos de suporte", "Pedidos de acesso temporário, aprovados pelo diretor da escola.", "P-11"),
      soon("contratos", "Contratos", "Pagantes, escolas cobertas, vigência e empenho.", "P-07"),
      soon("faturas", "Faturas", "Lançamento, baixa manual e nota fiscal.", "P-08"),
      soon("auditoria", "Auditoria", "Ações realizadas no painel da plataforma.", "F1-09"),
      { path: "contas", element: <AccountsPage /> },
      { path: "teste-email", element: <EmailTestPage /> },
      soon("configuracoes", "Configurações", "Planos, subdomínios reservados e parâmetros da plataforma.", "P-06"),
      soon("*", "Página não encontrada", "O endereço acessado não existe.", "—"),
    ],
  },
];
