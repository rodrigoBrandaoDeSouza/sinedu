import { Alert, api, ApiError, Button, Input } from "@sinedu/ui";
import { ArrowRight, KeyRound, ShieldCheck } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router";
import { AuthLayout } from "../components/auth-layout";
import { MfaEnrollment } from "../components/mfa-enrollment";
import { getSession, safeReturnUrl, type AuthStatus } from "../lib/session";

type Step = "loading" | "password" | "mfa" | "enroll";

export function LoginPage() {
  const [params] = useSearchParams();
  const returnUrl = safeReturnUrl(params.get("returnUrl"));
  const reauth = params.get("reauth") === "1";
  const [step, setStep] = useState<Step>("loading");

  const finish = () => window.location.assign(returnUrl);

  useEffect(() => {
    getSession()
      .then((s) => {
        if (s.authenticated && !reauth) return finish();
        setStep(s.pendingStage === "mfa" ? "mfa" : s.pendingStage === "enroll" ? "enroll" : "password");
      })
      .catch(() => setStep("password"));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onStatus = (status: AuthStatus) => {
    if (status === "signed_in") finish();
    else setStep(status === "mfa_required" ? "mfa" : "enroll");
  };

  if (step === "loading") {
    return <AuthLayout title="Entrar">{null}</AuthLayout>;
  }

  if (step === "enroll") {
    return (
      <AuthLayout
        title="Ative a verificação em duas etapas"
        description="Seu acesso inclui dados protegidos. Use um aplicativo autenticador (Google Authenticator, Microsoft Authenticator, 1Password…)."
      >
        <MfaEnrollment onDone={finish} />
      </AuthLayout>
    );
  }

  if (step === "mfa") {
    return (
      <AuthLayout title="Verificação em duas etapas" description="Digite o código de 6 dígitos do seu aplicativo autenticador.">
        <MfaForm onSignedIn={finish} />
      </AuthLayout>
    );
  }

  return (
    <AuthLayout
      title={reauth ? "Confirme sua identidade" : "Entrar no Sinedu"}
      description={reauth ? "Por segurança, entre novamente para acessar esta área." : "Use a conta que você recebeu por convite da sua escola."}
      footer={<Link to="/esqueci-senha" className="font-medium text-primary hover:underline">Esqueci minha senha</Link>}
    >
      <PasswordForm onStatus={onStatus} />
    </AuthLayout>
  );
}

function PasswordForm({ onStatus }: { onStatus: (status: AuthStatus) => void }) {
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    setBusy(true);
    setError(undefined);
    try {
      const result = await api.post<{ status: AuthStatus }>("/api/auth/login", { email: data.get("email"), password: data.get("password") });
      onStatus(result.status);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Não foi possível entrar. Tente novamente.");
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      {error ? <Alert tone="critical" title={error} /> : null}
      <Input label="E-mail" name="email" type="email" autoComplete="username" required autoFocus />
      <Input label="Senha" name="password" type="password" autoComplete="current-password" required />
      <Button type="submit" variant="primary" size="lg" className="w-full" disabled={busy}>
        {busy ? "Entrando…" : "Entrar"}
        <ArrowRight />
      </Button>
    </form>
  );
}

function MfaForm({ onSignedIn }: { onSignedIn: () => void }) {
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [useRecovery, setUseRecovery] = useState(false);

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const code = new FormData(e.currentTarget).get("code");
    setBusy(true);
    setError(undefined);
    try {
      await api.post("/api/auth/mfa/verify", { code });
      onSignedIn();
    } catch (err) {
      if (err instanceof ApiError && err.code === "session_expired") window.location.reload();
      setError(err instanceof ApiError ? err.message : "Código inválido.");
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      {error ? <Alert tone="critical" title={error} /> : null}
      {useRecovery ? (
        <Input key="recovery" label="Código de recuperação" name="code" autoComplete="off" placeholder="xxxxx-xxxxx" required autoFocus />
      ) : (
        <Input
          key="totp"
          label="Código"
          name="code"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9]*"
          maxLength={6}
          placeholder="000000"
          className="[&_input]:text-center [&_input]:text-lg [&_input]:tracking-[0.4em]"
          required
          autoFocus
        />
      )}
      <Button type="submit" variant="primary" size="lg" className="w-full" disabled={busy}>
        <ShieldCheck />
        {busy ? "Verificando…" : "Verificar"}
      </Button>
      <button type="button" className="inline-flex w-full items-center justify-center gap-1.5 text-sm text-muted-foreground hover:text-foreground" onClick={() => setUseRecovery((v) => !v)}>
        <KeyRound className="size-3.5" aria-hidden />
        {useRecovery ? "Usar o aplicativo autenticador" : "Perdeu o celular? Usar código de recuperação"}
      </button>
    </form>
  );
}
