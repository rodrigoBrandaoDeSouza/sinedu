using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sinedu.SharedKernel.Web;
using Sinedu.Tenant.Access;

namespace Sinedu.Tenant.School;

/// <summary>
/// O que a pessoa logada enxerga nesta escola (matriz de permissões + P2/P6/P7).
/// Calculado uma vez por requisição a partir dos papéis atuais no banco da escola.
/// </summary>
public sealed record ViewerScope(
    Guid UserId,
    bool AllClassrooms,
    IReadOnlySet<Guid> ClassroomIds,
    bool SeesStudents,
    bool SeesGuardians,
    bool SeesAddress,
    bool CanEditStudents,
    bool CanManageStructure)
{
    public bool CanSeeClassroom(Guid classroomId) => AllClassrooms || ClassroomIds.Contains(classroomId);
}

public interface IViewerScopeProvider
{
    Task<ViewerScope> GetAsync(CancellationToken ct = default);
}

internal sealed class ViewerScopeProvider(
    IHttpContextAccessorWrapper http, SchoolDbContext school, AccessDbContext access, TimeProvider clock) : IViewerScopeProvider
{
    private ViewerScope? _cached;

    public async Task<ViewerScope> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var user = http.User;
        var userId = Guid.Parse(user.FindFirstValue("sub")!);
        var roles = user.FindAll("role").Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

        var pedagogical = roles.Contains(Roles.Director) || roles.Contains(Roles.Pedagogue);
        var support = roles.Contains(Roles.PlatformSupport);
        var techAdmin = roles.Contains(Roles.TechAdmin);
        var all = pedagogical || support || techAdmin;

        var classrooms = new HashSet<Guid>();
        if (!all)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (roles.Contains(Roles.Teacher))
            {
                classrooms.UnionWith(await school.TeacherAssignments.AsNoTracking()
                    .Where(t => t.StaffMember!.UserId == userId && t.StaffMember.Active && (t.EndsOn == null || t.EndsOn >= today))
                    .Where(t => school.Classrooms.Any(c => c.Id == t.ClassroomId && c.Status == ClassroomStatus.Active
                        && c.SchoolYear!.Status == SchoolYearStatus.Active))
                    .Select(t => t.ClassroomId)
                    .ToListAsync(ct));
            }

            if (roles.Contains(Roles.ReadOnly))
            {
                classrooms.UnionWith(await access.ClassroomGrants.AsNoTracking()
                    .Where(g => g.UserId == userId && g.RevokedAt == null)
                    .Select(g => g.ClassroomId)
                    .ToListAsync(ct));
            }
        }

        _cached = new ViewerScope(
            userId,
            AllClassrooms: all,
            ClassroomIds: classrooms,
            // P6: Admin técnico vê a estrutura, não os alunos.
            SeesStudents: pedagogical || support || roles.Contains(Roles.Teacher) || roles.Contains(Roles.ReadOnly),
            // P2: professor vê responsáveis e contatos; Consulta não.
            SeesGuardians: pedagogical || support || roles.Contains(Roles.Teacher),
            SeesAddress: pedagogical || support,
            CanEditStudents: pedagogical,
            CanManageStructure: roles.Contains(Roles.Director) || techAdmin);
        return _cached;
    }
}

/// <summary>Abstração mínima para facilitar testes do escopo.</summary>
public interface IHttpContextAccessorWrapper
{
    ClaimsPrincipal User { get; }
}

internal sealed class HttpContextAccessorWrapper(Microsoft.AspNetCore.Http.IHttpContextAccessor accessor) : IHttpContextAccessorWrapper
{
    public ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();
}
