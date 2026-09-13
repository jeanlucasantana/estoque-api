using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Estoque.Api.Data;

// Usada só pelas ferramentas do EF Core (migrations add, migrations bundle), que assim não precisam subir a aplicação
// nem a configuração obrigatória dela. Gerar migração não conecta no banco; o bundle recebe a conexão real via --connection.
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=estoque")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options);
    }
}
