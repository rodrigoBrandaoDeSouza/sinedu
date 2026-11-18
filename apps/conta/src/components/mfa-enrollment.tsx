import { Alert, api, ApiError, Button, Input } from "@sinedu/ui";
import { Check, Copy, Download } from "lucide-react";
import QRCode from "qrcode";
import { useEffect, useState, type FormEvent } from "react";

interface Setup {
  secret: string;
  otpAuthUri: string;
}

/** Cadastro do TOTP: QR code gerado localmente (o segredo nunca sai do navegador para terceiros). */
export function MfaEnrollment({ onDone }: { onDone: () => void }) {
  const [setup, setSetup] = useState<Setup>();
  const [qr, setQr] = useState<string>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [codes, setCodes] = useState<string[]>();

  useEffect(() => {
    api
      .post<Setup>("/api/auth/mfa/setup")
      .then(async (s) => {
        setSetup(s);
        const svg = await QRCode.toString(s.otpAuthUri, { type: "svg", margin: 1, errorCorrectionLevel: "M" });
        setQr(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "Não foi possível iniciar o cadastro."));
  }, []);

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.post<{ recoveryCodes: string[] }>("/api/auth/mfa/enable", { code: new FormData(e.currentTarget).get("code") });
      setCodes(result.recoveryCodes);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Código inválido.");
      setBusy(false);
    }
  };

  if (codes) {
    return <RecoveryCodes codes={codes} onDone={onDone} />;
  }

  return (
    <div className="space-y-5">
      {error ? <Alert tone="critical" title={error} /> : null}
      <ol className="space-y-5 text-sm">
        <li>
          <p className="font-medium text-foreground">1. Escaneie o QR code no aplicativo</p>
          <div className="mt-3 flex flex-col items-center gap-3 rounded-xl border border-border bg-white p-4">
            {qr ? <img src={qr} alt="QR code para o aplicativo autenticador" className="size-44" /> : <div className="size-44 animate-pulse rounded bg-surface-sunken" />}
          </div>
          {setup ? (
            <details className="mt-2 text-xs text-muted-foreground">
              <summary className="cursor-pointer">Não consegue escanear? Digite a chave</summary>
              <code className="mt-2 block break-all rounded-md bg-surface-sunken px-2 py-1.5 font-mono text-foreground">{setup.secret}</code>
            </details>
          ) : null}
        </li>
        <li>
          <p className="font-medium text-foreground">2. Digite o código gerado</p>
          <form onSubmit={submit} className="mt-3 space-y-3" noValidate>
            <Input
              label="Código de 6 dígitos"
              name="code"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={6}
              placeholder="000000"
              className="[&_input]:text-center [&_input]:text-lg [&_input]:tracking-[0.4em]"
              required
            />
            <Button type="submit" variant="primary" size="lg" className="w-full" disabled={busy || !setup}>
              {busy ? "Ativando…" : "Ativar verificação"}
            </Button>
          </form>
        </li>
      </ol>
    </div>
  );
}

function RecoveryCodes({ codes, onDone }: { codes: string[]; onDone: () => void }) {
  const [copied, setCopied] = useState(false);
  const text = codes.join("\n");

  const download = () => {
    const url = URL.createObjectURL(new Blob([`Códigos de recuperação Sinedu\n\n${text}\n`], { type: "text/plain" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = "sinedu-codigos-recuperacao.txt";
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="space-y-4">
      <Alert tone="warning" title="Guarde estes códigos agora">
        Cada código funciona uma única vez, se você perder o celular. Eles não serão mostrados de novo.
      </Alert>
      <ul className="grid grid-cols-2 gap-2 rounded-xl border border-border bg-surface-sunken p-4 font-mono text-sm text-foreground">
        {codes.map((c) => (
          <li key={c}>{c}</li>
        ))}
      </ul>
      <div className="flex gap-2">
        <Button
          className="flex-1"
          onClick={async () => {
            await navigator.clipboard.writeText(text);
            setCopied(true);
          }}
        >
          {copied ? <Check /> : <Copy />}
          {copied ? "Copiados" : "Copiar"}
        </Button>
        <Button className="flex-1" onClick={download}>
          <Download />
          Baixar
        </Button>
      </div>
      <Button variant="primary" size="lg" className="w-full" onClick={onDone}>
        Já guardei, continuar
      </Button>
    </div>
  );
}
