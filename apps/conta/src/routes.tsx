import type { RouteObject } from "react-router";
import { ForgotPasswordPage, HomePage, InvitationPage, NoAccessPage, ResetPasswordPage } from "./pages/account-pages";
import { LoginPage } from "./pages/login-page";

export const routes: RouteObject[] = [
  { index: true, element: <HomePage /> },
  { path: "entrar", element: <LoginPage /> },
  { path: "convite", element: <InvitationPage /> },
  { path: "esqueci-senha", element: <ForgotPasswordPage /> },
  { path: "redefinir-senha", element: <ResetPasswordPage /> },
  { path: "sem-acesso", element: <NoAccessPage /> },
  { path: "*", element: <HomePage /> },
];
