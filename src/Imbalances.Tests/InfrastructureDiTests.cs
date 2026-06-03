using Imbalances.Core.Services;
using Imbalances.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imbalances.Tests;

public class InfrastructureDiTests
{
    [Fact]
    public void AddInfrastructureServices_RegistraAuditoriaService()
    {
        var services = new ServiceCollection();
        services.AddInfrastructureServices();
        using var provider = services.BuildServiceProvider();

        var auditoria = provider.GetService<IAuditoriaService>();

        Assert.NotNull(auditoria);
    }
}

