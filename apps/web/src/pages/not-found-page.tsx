import { Button, EmptyState } from "@sinedu/ui";
import { Compass } from "lucide-react";
import { Link } from "react-router";

export function NotFoundPage() {
  return (
    <EmptyState
      icon={Compass}
      title="Página não encontrada"
      description="O endereço acessado não existe ou você não tem acesso a ele."
      action={
        <Link to="/">
          <Button variant="primary">Voltar ao início</Button>
        </Link>
      }
    />
  );
}
