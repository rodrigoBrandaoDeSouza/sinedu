import { Check, Monitor, Moon, Sun } from "lucide-react";
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { cn } from "../lib/cn";

export type ThemePreference = "light" | "dark" | "system";
type ResolvedTheme = "light" | "dark";

/** Cores base oferecidas em Configurações. Os tons de cada uma ficam em theme.css (data-accent). */
export const accents = [
  { value: "indigo", label: "Índigo" },
  { value: "azul", label: "Azul" },
  { value: "petroleo", label: "Petróleo" },
  { value: "verde", label: "Verde" },
  { value: "violeta", label: "Violeta" },
  { value: "magenta", label: "Magenta" },
  { value: "grafite", label: "Grafite" },
] as const;
export type Accent = (typeof accents)[number]["value"];

interface ThemeContextValue {
  preference: ThemePreference;
  resolved: ResolvedTheme;
  setPreference: (preference: ThemePreference) => void;
  accent: Accent;
  setAccent: (accent: Accent) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);
const STORAGE_KEY = "sinedu.theme";
const ACCENT_KEY = "sinedu.accent";

function read(key: string): string | null {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string) {
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Armazenamento indisponível (janela privada): segue só na sessão.
  }
}

function readStoredPreference(): ThemePreference {
  const value = read(STORAGE_KEY);
  return value === "light" || value === "dark" || value === "system" ? value : "system";
}

function readStoredAccent(): Accent {
  const value = read(ACCENT_KEY);
  return accents.find((a) => a.value === value)?.value ?? "indigo";
}

function systemTheme(): ResolvedTheme {
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>(readStoredPreference);
  const [accent, setAccentState] = useState<Accent>(readStoredAccent);
  const [system, setSystem] = useState<ResolvedTheme>(systemTheme);

  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setSystem(media.matches ? "dark" : "light");
    media.addEventListener("change", onChange);
    return () => media.removeEventListener("change", onChange);
  }, []);

  const resolved: ResolvedTheme = preference === "system" ? system : preference;

  useEffect(() => {
    document.documentElement.dataset.theme = resolved;
  }, [resolved]);

  useEffect(() => {
    document.documentElement.dataset.accent = accent;
  }, [accent]);

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next);
    write(STORAGE_KEY, next);
  }, []);

  const setAccent = useCallback((next: Accent) => {
    setAccentState(next);
    write(ACCENT_KEY, next);
  }, []);

  const value = useMemo(() => ({ preference, resolved, setPreference, accent, setAccent }), [preference, resolved, setPreference, accent, setAccent]);
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  const context = useContext(ThemeContext);
  if (!context) throw new Error("useTheme precisa estar dentro de <ThemeProvider>.");
  return context;
}

const options: { value: ThemePreference; label: string; icon: typeof Sun }[] = [
  { value: "light", label: "Claro", icon: Sun },
  { value: "dark", label: "Escuro", icon: Moon },
  { value: "system", label: "Sistema", icon: Monitor },
];

export function ThemeToggle({ className }: { className?: string }) {
  const { preference, setPreference } = useTheme();
  return (
    <div
      role="radiogroup"
      aria-label="Tema"
      className={cn("inline-flex items-center gap-0.5 rounded-lg border border-border bg-surface-sunken p-0.5", className)}
    >
      {options.map(({ value, label, icon: Icon }) => (
        <button
          key={value}
          type="button"
          role="radio"
          aria-checked={preference === value}
          title={label}
          onClick={() => setPreference(value)}
          className={cn(
            "grid size-7 place-items-center rounded-md text-muted-foreground transition-colors hover:text-foreground",
            preference === value && "bg-surface-raised text-foreground shadow-card",
          )}
        >
          <Icon className="size-3.5" aria-hidden />
          <span className="sr-only">{label}</span>
        </button>
      ))}
    </div>
  );
}

/** Configurações → Aparência: tema (claro/escuro/sistema) e cor base. Preferência deste navegador. */
export function AppearanceSettings() {
  const { preference, setPreference, accent, setAccent } = useTheme();
  return (
    <div className="space-y-6">
      <fieldset>
        <legend className="text-sm font-medium text-foreground">Tema</legend>
        <p className="mt-0.5 text-xs text-muted-foreground">"Sistema" acompanha a configuração do seu computador ou celular.</p>
        <div role="radiogroup" aria-label="Tema" className="mt-3 grid max-w-md grid-cols-3 gap-2">
          {options.map(({ value, label, icon: Icon }) => (
            <button
              key={value}
              type="button"
              role="radio"
              aria-checked={preference === value}
              onClick={() => setPreference(value)}
              className={cn(
                "flex h-10 items-center justify-center gap-2 rounded-lg border border-border-strong bg-surface-raised text-sm text-foreground-secondary transition-colors hover:border-foreground/30 hover:text-foreground",
                preference === value && "border-primary bg-primary-soft text-primary-soft-foreground hover:border-primary hover:text-primary-soft-foreground",
              )}
            >
              <Icon className="size-4" aria-hidden />
              {label}
            </button>
          ))}
        </div>
      </fieldset>

      <fieldset>
        <legend className="text-sm font-medium text-foreground">Cor base</legend>
        <p className="mt-0.5 text-xs text-muted-foreground">Botões, links, menu e destaques. Gráficos e alertas mantêm as cores próprias.</p>
        <div role="radiogroup" aria-label="Cor base" className="mt-3 flex flex-wrap gap-3">
          {accents.map(({ value, label }) => {
            const selected = accent === value;
            return (
              <button
                key={value}
                type="button"
                role="radio"
                aria-checked={selected}
                onClick={() => setAccent(value)}
                className={cn(
                  "flex w-20 flex-col items-center gap-1.5 rounded-lg border border-transparent p-2 text-xs text-foreground-secondary transition-colors hover:bg-surface-sunken hover:text-foreground",
                  selected && "border-border-strong bg-surface-sunken text-foreground",
                )}
              >
                <span data-accent={value} className="accent-swatch grid size-9 place-items-center rounded-full shadow-card">
                  {selected ? <Check className="size-4 text-primary-foreground" strokeWidth={3} aria-hidden /> : null}
                </span>
                {label}
              </button>
            );
          })}
        </div>
      </fieldset>
    </div>
  );
}
