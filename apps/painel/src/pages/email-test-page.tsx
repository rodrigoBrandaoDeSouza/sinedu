import { useMutation, useQuery } from "@tanstack/react-query";
import { Alert, api, ApiError, Button, Card, CardBody, CardHeader, Input, PageHeader, Select } from "@sinedu/ui";
import { MailCheck, Send } from "lucide-react";
import { useState } from "react";
import { TestResult } from "../components/smtp-dialog";
import { smtpApi, type EmailTestResult } from "../lib/smtp-api";
import { useIsSuperAdmin } from "../lib/session";

interface TenantOption {
  id: string;
  displayName: string;
  slug: string;
  status: string;
}

/**
 * Área de testes de e-mail: envia uma mensagem de teste pelo SMTP de uma escola (ou o padrão da plataforma)
 * e mostra o que o servidor respondeu. O histórico fica só nesta tela (não é salvo).
 */
export function EmailTestPage() {
  const superAdmin = useIsSuperAdmin();
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: () => api.get<TenantOption[]>("/api/tenants"), enabled: superAdmin });
  const [tenantId, setTenantId] = useState("");
  const [to, setTo] = useState("");
  const [history, setHistory] = useState<(EmailTestResult & { to: string; at: Date })[]>([]);
  const send = useMutation({
    mutationFn: () => smtpApi.test({ tenantId: tenantId || null, to: to.trim() }),
    onSuccess: (r) => setHistory((h) => [{ ...r, to: to.trim(), at: new Date() }, ...h].slice(0, 20)),
  });

  if (!superAdmin) return <Alert tone="warning" title="Somente super admins testam o envio de e-mail." />;

  return (
    <>
      <PageHeader title="Teste de e-mail" description="Envie uma mensagem de teste pelo SMTP de uma escola ou pelo padrão da plataforma e veja a resposta do servidor." />
      <div className="grid gap-4 xl:grid-cols-3">
        <Card>
          <CardHeader title="Enviar teste" description="Use um endereço seu. A mensagem não tem dado de aluno." />
          <CardBody>
            <form
              className="space-y-4"
              noValidate
              onSubmit={(e) => {
                e.preventDefault();
                if (to.trim()) send.mutate();
              }}
            >
              <Select
                label="Enviar por"
                value={tenantId}
                onChange={(e) => setTenantId(e.target.value)}
                options={[
                  { value: "", label: "SMTP padrão da plataforma" },
                  ...(tenants.data ?? []).filter((t) => t.status !== "Closed").map((t) => ({ value: t.id, label: `${t.displayName} (${t.slug})` })),
                ]}
              />
              <Input label="Destinatário" type="email" value={to} onChange={(e) => setTo(e.target.value)} placeholder="voce@exemplo.com.br" />
              {send.error ? <Alert tone="critical" title={send.error instanceof ApiError ? send.error.message : "Não foi possível testar."} /> : null}
              <Button variant="primary" type="submit" className="w-full" disabled={send.isPending || !to.trim()}>
                <Send /> {send.isPending ? "Enviando…" : "Enviar e-mail de teste"}
              </Button>
              <p className="text-xs text-muted-foreground">
                Escola sem SMTP próprio usa o padrão. Para configurar o de uma escola, use o botão <strong>E-mail</strong> em Escolas. Em desenvolvimento, o padrão é o Mailpit
                (http://localhost:8025).
              </p>
            </form>
          </CardBody>
        </Card>

        <Card className="xl:col-span-2">
          <CardHeader title="Resultados" description="Últimos testes desta sessão" />
          <CardBody>
            {history.length === 0 ? (
              <div className="flex flex-col items-center py-10 text-center text-sm text-muted-foreground">
                <MailCheck className="mb-2 size-8" aria-hidden />
                Nenhum teste ainda.
              </div>
            ) : (
              <ul className="space-y-3">
                {history.map((h, i) => (
                  <li key={i}>
                    <p className="text-xs text-muted-foreground">
                      {h.at.toLocaleTimeString("pt-BR")} · {h.school} → {h.to}
                    </p>
                    <TestResult result={h} />
                  </li>
                ))}
              </ul>
            )}
          </CardBody>
        </Card>
      </div>
    </>
  );
}
