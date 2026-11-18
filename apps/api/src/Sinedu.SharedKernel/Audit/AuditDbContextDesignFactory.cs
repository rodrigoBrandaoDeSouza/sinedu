using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Sinedu.SharedKernel.Persistence;

namespace Sinedu.SharedKernel.Audit;

/// <summary>Mesma migration serve ao banco da plataforma e aos bancos das escolas.</summary>
public sealed class AuditDbContextDesignFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseSineduPostgres("Host=localhost;Database=design", AuditDbContext.Schema)
            .Options);
}
