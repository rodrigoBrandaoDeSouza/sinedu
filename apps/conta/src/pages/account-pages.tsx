import { Alert, api, ApiError, Button, Input, postNavigate } from "@sinedu/ui";
import { ArrowUpRight, Building2, Gauge, LogOut, ShieldAlert } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate, useSearchParams } from "react-router";
import { AuthLayout } from "../components/auth-layout";
import { MfaEnrollment } from "../components/mfa-enrollment";
import { getSession, type AuthStatus, type SessionInfo } from "../lib/session";

/** Início: escolas às quais a conta tem acesso. */
export function HomePage() {
  const navigate = useNavigate();
  const [session, setSession] = useState<SessionInfo>();

  useEffect(() => {
    getSession().then((s) => (s.authenticated ? setSession(s) : navigate("/entrar", { replace: true })));
  }, [navigate]);

  const logout = async () => {
    await api.post("/api/auth/logout");
    navigate("/entrar", { replace: true });
  };

  if (!session?.user) return <AuthLayout title="Suas escolas">{null}</AuthLayout>;

  const schools = session.schools ?? [];
  return (
    <AuthLayout
      title={`Olá, ${session.user.fullName.split(" ")[0]}`}
      description={schools.length ? "Escolha onde você quer entrar." : "Sua conta ainda não tem acesso a nenhuma escola."}
      footer={
        <button type="button" onClick={logout} className="inline-flex items-center gap-1.5 font-medium hover:text-foreground">
          <LogOut className="size-3.5" aria-hidden />
          Sair de {session.user.email}
        </button>
      }
    >
      <ul className="space-y-2">
        {session.panel ? (
          <li>
            <Destination href={session.panel} icon={Gauge} title="Painel da plataforma" subtitle="Escolas, contratos e faturas" />
          </li>
        ) : null}
        {schools.map((s) => (
          <li key={s.slug}>
            <Destination href={s.url} icon={Building2} title={s.name} subtitle={new URL(s.url).host} />
          </li>
        ))}
      </ul>
    </AuthLayout>
  );
}

function Destination({ href, icon: Icon, title, subtitle }: { href: string; icon: typeof Building2; title: string; subtitle: string }) {
  return (
    <a
      href={href}
      className="group flex items-center gap-3 rounded-xl border border-border bg-surface-raised p-3 transition-colors hover:border-primary/40 hover:bg-primary-soft/40"
    >
      <span className="grid size-10 shrink-0 place-items-center rounded-lg bg-primary-soft text-primary-soft-foreground">
        <Icon className="size-5" aria-hidden />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-sm font-medium text-foreground">{title}</span>
        <span className="block truncate text-xs text-muted-foreground">{subtitle}</span>
      </span>
      <ArrowUpRight className="size-4 text-muted-foreground transition-transform group-hover:-translate-y-0.5 group-hover:translate-x-0.5" aria-hidden />
    </a>
  );
}

export function InvitationPage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const [invite, setInvite] = useState<{ email: string; fullName: string; schools: string[] } | null>();
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [enroll, setEnroll] = useState(false);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.get<{ email: string; fullName: string; schools: string[] }>(`/api/auth/invitations/${encodeURIComponent(token)}`).then(setInvite).catch(() => setInvite(null));
  }, [token]);

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    if (data.get("password") !== data.get("confirm")) return setErrors({ confirm: "As senhas não conferem." });
    setBusy(true);
    setErrors({});
    try {
      const result = await api.post<{ status: AuthStatus }>("/api/auth/invitations/accept", {
        token,
        fullName: data.get("fullName"),
        password: data.get("password"),
      });
      if (result.status === "signed_in") window.location.assign("/");
      else setEnroll(true);
    } catch (err) {
      setErrors(err instanceof ApiError ? { password: err.fieldErrors.password?.[0] ?? err.message } : { password: "Não foi possível concluir." });
      setBusy(false);
    }
  };

  if (enroll) {
    return (
      <AuthLayout title="Ative a verificação em duas etapas" description="Seu papel na escola dá acesso a dados protegidos, então este passo é obrigatório.">
        <MfaEnrollment onDone={() => window.location.assign("/")} />
      </AuthLayout>
    );
  }

  if (invite === null) return <InvalidLink />;
  if (!invite) return <AuthLayout title="Convite">{null}</AuthLayout>;

  return (
    <AuthLayout title="Crie sua senha" description={`Convite para ${invite.schools.join(", ") || "o Sinedu"}.`}>
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="E-mail" value={invite.email} readOnly disabled />
        <Input label="Seu nome" name="fullName" defaultValue={invite.fullName} autoComplete="name" required />
        <Input label="Senha" name="password" type="password" autoComplete="new-password" hint="Pelo menos 12 caracteres." error={errors.password} required />
        <Input label="Confirme a senha" name="confirm" type="password" autoComplete="new-password" error={errors.confirm} required />
        <Button type="submit" variant="primary" size="lg" className="w-full" disabled={busy}>
          {busy ? "Salvando…" : "Criar senha e entrar"}
        </Button>
      </form>
    </AuthLayout>
  );
}

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false);
  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    await api.post("/api/auth/password/forgot", { email: new FormData(e.currentTarget).get("email") }).catch(() => undefined);
    setSent(true);
  };

  return (
    <AuthLayout
      title="Redefinir senha"
      description="Enviaremos um link para o e-mail da sua conta."
      footer={<Link to="/entrar" className="font-medium text-primary hover:underline">Voltar para entrar</Link>}
    >
      {sent ? (
        <Alert tone="good" title="Verifique seu e-mail">Se houver uma conta com esse endereço, o link chega em alguns minutos e vale por 30 minutos.</Alert>
      ) : (
        <form onSubmit={submit} className="space-y-4" noValidate>
          <Input label="E-mail" name="email" type="email" autoComplete="username" required autoFocus />
          <Button type="submit" variant="primary" size="lg" className="w-full">Enviar link</Button>
        </form>
      )}
    </AuthLayout>
  );
}

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [errors, setErrors] = useState<Record<string, string>>({});

  const submit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    const data = new FormData(e.currentTarget);
    if (data.get("password") !== data.get("confirm")) return setErrors({ confirm: "As senhas não conferem." });
    try {
      await api.post("/api/auth/password/reset", { token: params.get("token"), password: data.get("password") });
      navigate("/entrar", { replace: true });
    } catch (err) {
      setErrors({ password: err instanceof ApiError ? (err.fieldErrors.password?.[0] ?? err.message) : "Não foi possível redefinir." });
    }
  };

  return (
    <AuthLayout title="Nova senha">
      <form onSubmit={submit} className="space-y-4" noValidate>
        <Input label="Nova senha" name="password" type="password" autoComplete="new-password" hint="Pelo menos 12 caracteres." error={errors.password} required />
        <Input label="Confirme a senha" name="confirm" type="password" autoComplete="new-password" error={errors.confirm} required />
        <Button type="submit" variant="primary" size="lg" className="w-full">Salvar nova senha</Button>
      </form>
    </AuthLayout>
  );
}

export function NoAccessPage() {
  const [params] = useSearchParams();
  const school = params.get("escola");
  return (
    <AuthLayout title="Sem acesso" footer={<Link to="/" className="font-medium text-primary hover:underline">Ver minhas escolas</Link>}>
      <div className="flex gap-3">
        <ShieldAlert className="mt-0.5 size-5 shrink-0 text-critical-text" aria-hidden />
        <p className="text-sm text-foreground-secondary">
          {params.get("painel")
            ? "Sua conta não tem acesso ao painel da plataforma."
            : `Sua conta não tem acesso ${school ? `à escola “${school}”` : "a esta escola"}. Peça à direção da escola para enviar um convite.`}
        </p>
      </div>
      <Button className="mt-6 w-full" onClick={() => postNavigate("/connect/endsession")}>
        Entrar com outra conta
      </Button>
    </AuthLayout>
  );
}

function InvalidLink() {
  return (
    <AuthLayout title="Link inválido" footer={<Link to="/entrar" className="font-medium text-primary hover:underline">Ir para entrar</Link>}>
      <Alert tone="warning" title="Este link expirou ou já foi usado.">Peça um novo convite à direção da escola.</Alert>
    </AuthLayout>
  );
}
