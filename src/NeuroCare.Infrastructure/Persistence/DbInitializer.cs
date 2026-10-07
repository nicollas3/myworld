using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeuroCare.Domain;

namespace NeuroCare.Infrastructure;

public static class DbInitializer
{
    /// <summary>
    /// Aplica as migrations do banco e, opcionalmente,
    /// popula dados fictícios.
    ///
    /// Repete algumas vezes para tolerar o SQL Server ainda
    /// estar iniciando (docker-compose).
    /// </summary>
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration config,
        ILogger logger,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();

                var db = scope.ServiceProvider
                    .GetRequiredService<NeuroCareDbContext>();

                // Migrations são o único mecanismo utilizado
                // para criar e atualizar o banco.
                await db.Database.MigrateAsync(ct);

                // Garante que as roles necessárias existam.
                await EnsureRolesAsync(scope.ServiceProvider);

                // Executa o seed somente se estiver habilitado.
                if (config.GetValue<bool>("Seed:Enabled"))
                {
                    await DemoSeeder.SeedAsync(
                        scope.ServiceProvider,
                        config,
                        logger,
                        ct);
                }

                logger.LogInformation(
                    "Banco de dados inicializado com sucesso.");

                return;
            }
            catch (SqlException ex) when (ex.Number == 2714)
            {
                // SQL Server 2714:
                // "Já existe um objeto com nome '...' no banco de dados."
                //
                // Esse erro não é transitório. Não adianta repetir
                // a tentativa, pois a migration está incompatível
                // com o estado atual do banco.
                logger.LogError(
                    ex,
                    "Erro de migration: um objeto que a migration " +
                    "tentou criar já existe no banco de dados. " +
                    "Verifique o histórico das migrations e o banco.");

                throw;
            }
            catch (OperationCanceledException)
            {
                // Cancelamento não deve ser tratado como erro
                // nem gerar novas tentativas.
                throw;
            }
            catch (Exception ex) when (attempt < 10)
            {
                // Mantém as tentativas para problemas transitórios,
                // como SQL Server ainda não estar disponível.
                logger.LogWarning(
                    ex,
                    "Banco ainda indisponível " +
                    "(tentativa {Attempt}/10). " +
                    "Nova tentativa em 5 segundos.",
                    attempt);

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    ct);
            }
        }
    }

    /// <summary>
    /// Garante que todas as roles necessárias pelo NeuroCare
    /// existam no banco.
    /// </summary>
    private static async Task EnsureRolesAsync(IServiceProvider sp)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                var result = await roles.CreateAsync(
                    new IdentityRole<Guid>(role));

                if (!result.Succeeded)
                {
                    var errors = string.Join(
                        "; ",
                        result.Errors.Select(e =>
                            $"{e.Code}: {e.Description}"));

                    throw new InvalidOperationException(
                        $"Não foi possível criar a role '{role}'. " +
                        $"Erros: {errors}");
                }
            }
        }
    }
}