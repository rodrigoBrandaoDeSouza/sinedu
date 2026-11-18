import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, ApiError, Badge, Button, Dialog, Input } from "@sinedu/ui";
import { CheckCircle2, Send, Trash2, XCircle } from "lucide-react";
import { useRef, useState, type FormEvent } from "react";
import { smtpApi, type EmailTestResult, type SmtpInput } from "../lib/smtp-api";

const fieldErrors = (err: unknown): Record<string, string> =>
  err instanceof ApiError && Object.keys(err.fieldErrors).length
    ? Object.fromEntries(Object.entries(err.fieldErrors).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v[0] ?? ""]))
    : { form: err instanceof ApiError ? err.message : "Não foi possível salvar." };

/** SMTP próprio da escola: e-mails dela (convites, comunicações) saem por aqui. A senha nunca volta para a tela. */
export function SmtpDialog({ tenant, onClose }: { tenant: { id: string; displayName: string }; onClose: () => void }) {
  const client = useQueryClient();
  const form = useRef<HTMLFormElement>(null);
  const current = useQuery({ queryKey: ["smtp", tenant.id], queryFn: () => smtpApi.get(tenant.id) });
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [testTo, setTestTo] = useState("");
  const [result, setResult] = useState<EmailTestResult>();

  const read = (): SmtpInput => {
    const d = new FormData(form.current!);
    const text = (k: string) => String(d.get(k) ?? "").trim() || null;
    return {
      host: text("host") ?? "",
      port: Number(d.get("port")) || 587,
      useTls: d.get("useTls") === "on",
      username: text("username"),
      password: String(d.get("password") ?? "") || null,
      fromAddress: text("fromAddress") ?? "",
      fromName: text("fromName"),
    };
  };

  const save = useMutation({
    mutationFn: () => smtpApi.save(tenant.id, read()),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["smtp", tenant.id] });
      onClose();
    },
    onError: (err) => setErrors(fieldErrors(err)),
  });
  const remove = useMutation({
    mutationFn: () => smtpApi.remove(tenant.id),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ["smtp", tenant.id] });
      onClose();
    },
  });
  const test = useMutation({
    mutationFn: () => smtpApi.test({ tenantId: tenant.id, to: testTo.trim(), smtp: read() }),
    onSuccess: (r) => {
      setErrors({});
      setResult(r);
    },
    onError: (err) => setErrors(fieldErrors(err)),
  });

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setErrors({});
    save.mutate();
  };

  const c = current.data;
  return (
    <Dialog
      open
      onClose={onClose}
      title={`E-mail de ${tenant.displayName}`}
      description="Convites e comunicações desta escola saem por este servidor. Sem configuração, a escola usa o SMTP padrão da plataforma."
      footer={
        <>
          {c?.configured ? (
            <Button variant="ghost" onClick={() => confirm("Voltar a usar o SMTP padrão da plataforma?") && remove.mutate()} className="mr-auto">
              <Trash2 /> Usar o padrão
            </Button>
          ) : null}
          <Button onClick={onClose}>Cancelar</Button>
          <Button variant="primary" type="submit" form="smtp-form" disabled={save.isPending || !c}>
            Salvar
          </Button>
        </>
      }
    >
      {!c ? (
        <div className="h-60 animate-pulse rounded-lg bg-surface-sunken" />
      ) : (
        <form id="smtp-form" ref={form} onSubmit={submit} className="space-y-4" noValidate key={String(c.configured)}>
          {errors.form ? <Alert tone="critical" title={errors.form} /> : null}
          <Badge tone={c.configured ? "good" : "neutral"} icon>
            {c.configured ? "SMTP próprio configurado" : "Usando o SMTP padrão da plataforma"}
          </Badge>
          <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_7rem]">
            <Input label="Servidor SMTP" name="host" defaultValue={c.host ?? ""} placeholder="smtp.provedor.com.br" error={errors.host} />
            <Input label="Porta" name="port" type="number" defaultValue={c.port} error={errors.port} />
          </div>
          <label className="flex items-center gap-2 text-sm text-foreground">
            <input type="checkbox" name="useTls" defaultChecked={c.useTls} /> Usar TLS (587 com STARTTLS ou 465 com TLS direto)
          </label>
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="Usuário" name="username" defaultValue={c.username ?? ""} autoComplete="off" />
            <Input
              label="Senha"
              name="password"
              type="password"
              autoComplete="new-password"
              placeholder={c.hasPassword ? "•••••••• (manter a atual)" : ""}
              hint={c.hasPassword ? "Deixe em branco para manter a senha salva." : undefined}
            />
          </div>
          <div className="grid gap-4 sm:grid-cols-2">
            <Input label="E-mail do remetente" name="fromAddress" defaultValue={c.fromAddress ?? ""} placeholder="nao-responda@escola.edu.br" error={errors.fromAddress} />
            <Input label="Nome do remetente" name="fromName" defaultValue={c.fromName ?? tenant.displayName} />
          </div>

          <div className="rounded-xl border border-border bg-surface-sunken p-4">
            <p className="text-sm font-medium text-foreground">Testar antes de salvar</p>
            <p className="text-xs text-muted-foreground">Envia um e-mail de teste com os dados acima (não salva nada).</p>
            <div className="mt-3 flex flex-wrap items-end gap-2">
              <Input label="Enviar teste para" type="email" value={testTo} onChange={(e) => setTestTo(e.target.value)} className="min-w-60 flex-1" error={errors.to} />
              <Button type="button" onClick={() => test.mutate()} disabled={test.isPending || !testTo.trim()}>
                <Send /> {test.isPending ? "Enviando…" : "Enviar teste"}
              </Button>
            </div>
            {result ? <TestResult result={result} /> : null}
          </div>
        </form>
      )}
    </Dialog>
  );
}

export function TestResult({ result }: { result: EmailTestResult }) {
  return (
    <div className={`mt-3 rounded-lg border p-3 text-sm ${result.ok ? "border-good-text/30 bg-good-soft" : "border-critical-text/30 bg-critical-soft"}`}>
      <p className={`flex items-center gap-1.5 font-medium ${result.ok ? "text-good-text" : "text-critical-text"}`}>
        {result.ok ? <CheckCircle2 className="size-4" aria-hidden /> : <XCircle className="size-4" aria-hidden />}
        {result.ok ? "Enviado. Confira a caixa de entrada (e o spam)." : "Falhou"}
      </p>
      {result.error ? <p className="mt-1 text-critical-text">{result.error}</p> : null}
      <p className="mt-1 text-xs text-foreground-secondary">
        {result.source} · {result.server}
        {result.tls ? " · TLS" : ""} · {result.from} · {result.elapsedMs} ms
      </p>
    </div>
  );
}
