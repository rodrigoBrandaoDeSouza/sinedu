import { Check, ChevronDown } from "lucide-react";
import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from "react";
import { cn } from "../lib/cn";
import { fieldControl, fieldLabel, fieldLayout } from "./form";

export interface SelectOption {
  value: string;
  label: string;
}

/** Mesmo formato que as telas já usam do <select> nativo: `e.target.value`. */
export interface SelectChangeEvent {
  target: { value: string; name?: string };
  currentTarget: { value: string; name?: string };
}

export interface SelectProps {
  label: string;
  options: SelectOption[];
  /** Esconde o rótulo visualmente (continua acessível). */
  hideLabel?: boolean;
  value?: string;
  defaultValue?: string;
  onChange?: (event: SelectChangeEvent) => void;
  /** Nome do campo no <form> (enviado por um input oculto). */
  name?: string;
  disabled?: boolean;
  required?: boolean;
  error?: string;
  hint?: ReactNode;
  className?: string;
  id?: string;
}

const LIST_MAX_HEIGHT = 288;
// Sem a Popover API (navegador antigo, jsdom), a lista fica posicionada como "fixed" comum.
const supportsPopover = typeof HTMLElement !== "undefined" && "showPopover" in HTMLElement.prototype;

/**
 * Lista de seleção própria (o menu nativo do sistema não segue o tema).
 * Padrão ARIA "select-only combobox": o foco fica no botão e a opção ativa é anunciada por aria-activedescendant.
 * A lista abre na camada superior (Popover API), então funciona dentro de <dialog> e de áreas com rolagem.
 */
export function Select({ label, options, hideLabel = false, value, defaultValue, onChange, name, disabled, required, error, hint, className, id }: SelectProps) {
  const generated = useId();
  const buttonId = id ?? `select-${generated}`;
  const listId = `${buttonId}-list`;
  const controlled = value !== undefined;
  const [internal, setInternal] = useState(defaultValue);
  const current = controlled ? value : internal;
  const selectedIndex = Math.max(0, options.findIndex((o) => o.value === current));
  const selected = options[selectedIndex];

  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(selectedIndex);
  const [position, setPosition] = useState<CSSProperties>({});
  const buttonRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLUListElement>(null);
  const typeahead = useRef({ text: "", at: 0 });

  const place = useCallback(() => {
    const button = buttonRef.current;
    if (!button) return;
    const rect = button.getBoundingClientRect();
    const below = window.innerHeight - rect.bottom;
    const above = rect.top;
    const up = below < Math.min(LIST_MAX_HEIGHT, 200) && above > below;
    const width = Math.min(Math.max(rect.width, 200), window.innerWidth - 16);
    const left = Math.min(Math.max(8, rect.left), window.innerWidth - width - 8);
    setPosition({
      position: "fixed",
      left,
      minWidth: width,
      maxWidth: Math.max(width, 360),
      right: "auto",
      top: up ? "auto" : rect.bottom + 4,
      bottom: up ? window.innerHeight - rect.top + 4 : "auto",
      maxHeight: Math.min(LIST_MAX_HEIGHT, (up ? above : below) - 12),
      margin: 0,
    });
  }, []);

  // Abre na camada superior (acima de diálogos modais) quando o navegador suporta.
  useLayoutEffect(() => {
    const list = listRef.current;
    if (!list) return;
    if (open) {
      place();
      if (typeof list.showPopover === "function" && !list.matches(":popover-open")) list.showPopover();
    } else if (typeof list.hidePopover === "function" && list.matches(":popover-open")) {
      list.hidePopover();
    }
  }, [open, place]);

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (!buttonRef.current?.contains(target) && !listRef.current?.contains(target)) setOpen(false);
    };
    const onScroll = (e: Event) => {
      if (listRef.current?.contains(e.target as Node)) return;
      place();
    };
    document.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("resize", place);
    window.addEventListener("scroll", onScroll, true);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("resize", place);
      window.removeEventListener("scroll", onScroll, true);
    };
  }, [open, place]);

  useEffect(() => {
    if (!open) return;
    listRef.current?.querySelector<HTMLElement>(`[data-index="${active}"]`)?.scrollIntoView?.({ block: "nearest" });
  }, [open, active]);

  function show() {
    if (disabled || options.length === 0) return;
    setActive(selectedIndex);
    setOpen(true);
  }

  function choose(index: number) {
    const option = options[index];
    setOpen(false);
    buttonRef.current?.focus();
    if (!option || option.value === selected?.value) return;
    if (!controlled) setInternal(option.value);
    const target = { value: option.value, name };
    onChange?.({ target, currentTarget: target });
  }

  function findByText(key: string) {
    const now = Date.now();
    const t = typeahead.current;
    t.text = now - t.at > 700 ? key : t.text + key;
    t.at = now;
    const query = t.text.toLocaleLowerCase("pt-BR");
    const start = open ? active : selectedIndex;
    const ordered = [...options.slice(start + 1), ...options.slice(0, start + 1)];
    const match = ordered.find((o) => o.label.toLocaleLowerCase("pt-BR").startsWith(query));
    return match ? options.indexOf(match) : -1;
  }

  function onKeyDown(e: KeyboardEvent<HTMLButtonElement>) {
    if (disabled) return;
    const last = options.length - 1;
    if (!open) {
      if (["ArrowDown", "ArrowUp", "Enter", " "].includes(e.key)) {
        e.preventDefault();
        show();
      } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
        const index = findByText(e.key);
        if (index >= 0) {
          setActive(index);
          setOpen(true);
        }
      }
      return;
    }
    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        setActive((i) => Math.min(last, i + 1));
        break;
      case "ArrowUp":
        e.preventDefault();
        setActive((i) => Math.max(0, i - 1));
        break;
      case "Home":
      case "PageUp":
        e.preventDefault();
        setActive(0);
        break;
      case "End":
      case "PageDown":
        e.preventDefault();
        setActive(last);
        break;
      case "Enter":
      case " ":
        e.preventDefault();
        choose(active);
        break;
      case "Escape":
        // Fecha só a lista, não o diálogo em volta.
        e.preventDefault();
        e.stopPropagation();
        setOpen(false);
        break;
      case "Tab":
        setOpen(false);
        break;
      default:
        if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
          const index = findByText(e.key);
          if (index >= 0) setActive(index);
        }
    }
  }

  const describedBy = error ? `${buttonId}-error` : hint ? `${buttonId}-hint` : undefined;

  return (
    <div className={cn(fieldLayout, className)}>
      <label htmlFor={buttonId} className={cn(fieldLabel, hideLabel && "sr-only")}>
        {label}
      </label>
      <div className="relative">
        <button
          ref={buttonRef}
          id={buttonId}
          type="button"
          role="combobox"
          aria-haspopup="listbox"
          aria-expanded={open}
          aria-controls={listId}
          aria-activedescendant={open ? `${listId}-${active}` : undefined}
          aria-invalid={error ? true : undefined}
          aria-required={required || undefined}
          aria-describedby={describedBy}
          disabled={disabled}
          onClick={() => (open ? setOpen(false) : show())}
          onKeyDown={onKeyDown}
          className={cn(
            fieldControl,
            "flex h-10 items-center gap-2 pl-3 pr-9 text-left",
            open && "border-primary",
            error && "border-critical",
          )}
        >
          <span className="min-w-0 flex-1 truncate">{selected?.label ?? " "}</span>
        </button>
        <ChevronDown
          className={cn("pointer-events-none absolute right-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground transition-transform", open && "rotate-180")}
          aria-hidden
        />
        {name ? <input type="hidden" name={name} value={selected?.value ?? ""} /> : null}
        <ul
          ref={listRef}
          id={listId}
          role="listbox"
          aria-labelledby={buttonId}
          popover={supportsPopover ? "manual" : undefined}
          tabIndex={-1}
          hidden={!open}
          style={position}
          className="z-50 overflow-y-auto overscroll-contain rounded-xl border border-border bg-surface-raised p-1 text-sm text-foreground shadow-pop"
        >
          {options.map((option, index) => {
            const isSelected = index === selectedIndex;
            return (
              <li
                key={option.value}
                id={`${listId}-${index}`}
                data-index={index}
                role="option"
                aria-selected={isSelected}
                onMouseDown={(e) => e.preventDefault()}
                onMouseMove={() => setActive(index)}
                onClick={() => choose(index)}
                className={cn(
                  "flex cursor-pointer items-center gap-2 rounded-lg px-2.5 py-2",
                  index === active && "bg-surface-sunken",
                  isSelected && "font-medium text-primary-soft-foreground",
                )}
              >
                <span className="min-w-0 flex-1">{option.label}</span>
                {isSelected ? <Check className="size-4 shrink-0 text-primary" aria-hidden /> : <span className="size-4 shrink-0" aria-hidden />}
              </li>
            );
          })}
        </ul>
      </div>
      {error ? (
        <p id={`${buttonId}-error`} className="text-xs text-critical-text">
          {error}
        </p>
      ) : hint ? (
        <p id={`${buttonId}-hint`} className="text-xs text-muted-foreground">
          {hint}
        </p>
      ) : null}
    </div>
  );
}
