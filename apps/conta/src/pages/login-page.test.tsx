import { ThemeProvider } from "@sinedu/ui";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";
import { safeReturnUrl } from "../lib/session";
import { routes } from "../routes";

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  return render(
    <ThemeProvider>
      <RouterProvider router={router} />
    </ThemeProvider>,
  );
}

function mockFetch(handler: (url: string, init?: RequestInit) => unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, init?: RequestInit) => {
      const body = handler(url, init);
      return new Response(JSON.stringify(body ?? {}), { status: 200, headers: { "Content-Type": "application/json" } });
    }),
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("Login único", () => {
  it("pede o segundo fator depois da senha", async () => {
    mockFetch((url) => (url.endsWith("/session") ? { authenticated: false } : { status: "mfa_required" }));
    renderAt("/entrar");

    fireEvent.change(await screen.findByLabelText("E-mail"), { target: { value: "diretora@escola.local" } });
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "senha-forte-123" } });
    fireEvent.click(screen.getByRole("button", { name: /entrar/i }));

    expect(await screen.findByText("Verificação em duas etapas")).toBeInTheDocument();
  });

  it("mostra a mensagem de reautenticação no step-up", async () => {
    mockFetch(() => ({ authenticated: false }));
    renderAt("/entrar?reauth=1");

    expect(await screen.findByText("Confirme sua identidade")).toBeInTheDocument();
  });

  it("mostra erro genérico sem revelar se o e-mail existe", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) =>
        url.endsWith("/session")
          ? new Response(JSON.stringify({ authenticated: false }), { status: 200 })
          : new Response(JSON.stringify({ title: "E-mail ou senha incorretos.", code: "invalid_credentials" }), { status: 400 }),
      ),
    );
    renderAt("/entrar");

    fireEvent.change(await screen.findByLabelText("E-mail"), { target: { value: "x@y.z" } });
    fireEvent.change(screen.getByLabelText("Senha"), { target: { value: "errada" } });
    fireEvent.click(screen.getByRole("button", { name: /entrar/i }));

    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("E-mail ou senha incorretos."));
  });
});

describe("safeReturnUrl", () => {
  it("só aceita caminhos do próprio host", () => {
    expect(safeReturnUrl("/connect/authorize?client_id=x")).toBe("/connect/authorize?client_id=x");
    expect(safeReturnUrl("https://atacante.com")).toBe("/");
    expect(safeReturnUrl("//atacante.com")).toBe("/");
    expect(safeReturnUrl("/\\atacante.com")).toBe("/");
    expect(safeReturnUrl(null)).toBe("/");
  });
});
