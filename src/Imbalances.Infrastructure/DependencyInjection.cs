using Microsoft.Extensions.DependencyInjection;
using Imbalances.Core.Services;
using Imbalances.Infrastructure.Services;

namespace Imbalances.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<IProgressService, ProgressService>();
        services.AddScoped<IConfigService, ConfigService>();
        services.AddScoped<IEmpresaDetectionService, EmpresaDetectionService>();
        services.AddScoped<IAuditoriaService, AuditoriaService>();
        services.AddScoped<IMotor1Extractor, Motor1Extractor>();
        services.AddScoped<IExtractorEngine, ExtractorEngine>();
        services.AddScoped<IExcelProvider, ExcelProvider>();

        return services;
    }
}
