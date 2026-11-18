using Sinedu.SharedKernel.Tenancy;

namespace Sinedu.Tenant.Migrations;

/// <summary>
/// View <c>metrics.v_counts</c> (P-10, P13): o ÚNICO objeto de um banco de escola que a plataforma lê. Só números
/// agregados da escola inteira — nenhum nome, nenhum identificador de aluno e nada de Saúde ou Proteção (nem contagens).
/// A view roda com os privilégios do dono; o papel <c>sinedu_metrics</c> não lê tabela nenhuma.
/// </summary>
internal static class MetricsView
{
    public static readonly string[] Statements =
    [
        "CREATE SCHEMA IF NOT EXISTS metrics",
        """
        CREATE OR REPLACE VIEW metrics.v_counts AS
        SELECT
          (SELECT count(*) FROM students.students WHERE status = 'Active') AS active_students,
          (SELECT count(*) FROM school.classrooms c JOIN school.school_years y ON y.id = c.school_year_id
             WHERE y.status = 'Active' AND c.status = 'Active') AS active_classrooms,
          (SELECT count(*) FROM access.members WHERE status = 'Active') AS active_members,
          (SELECT count(*) FROM occurrences.occurrences
             WHERE status NOT IN ('Rascunho', 'Cancelada') AND occurred_at >= now() - interval '30 days') AS occurrences_last_30_days,
          (SELECT count(*) FROM notifications.notifications
             WHERE status = 'Enviado' AND sent_at >= date_trunc('month', now())) AS emails_sent_this_month,
          (SELECT count(*) FROM notifications.notifications WHERE status = 'Falhou') AS emails_failed_open,
          (SELECT coalesce(sum(size_bytes), 0) FROM occurrences.attachments WHERE removed_at IS NULL) AS common_storage_bytes,
          (SELECT version FROM infra.schema_info ORDER BY applied_at DESC LIMIT 1) AS schema_version
        """,
        // Papel de métricas (criado pela plataforma no `migrate`): conecta e lê só a view.
        $"""
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{MetricsContract.Role}') THEN
            EXECUTE format('GRANT CONNECT ON DATABASE %I TO {MetricsContract.Role}', current_database());
            GRANT USAGE ON SCHEMA metrics TO {MetricsContract.Role};
            GRANT SELECT ON metrics.v_counts TO {MetricsContract.Role};
          END IF;
        END $$;
        """,
    ];
}
