using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Bunit;
using Bunit.JSInterop;
using Imbalances.Client.Layout;
using Imbalances.Client.Pages;
using Imbalances.Client.Services;
using Imbalances.Client.UI.Core;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace Imbalances.Tests;

public class AnalisisPageTests
{
    [Fact]
    public async Task MainLayout_ExponeEnlacesDeNavegacionPrincipales()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();
        ctx.Services.AddScoped<ExplorerStateService>();
        ctx.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost/") });
        ctx.Services.AddScoped<UiConfigService>();
        ctx.Services.AddScoped<UiSnapshotService>();

        var cut = ctx.Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, (builder) => { }));

        Assert.Contains("/analisis", cut.Markup);
        Assert.Contains("/informe", cut.Markup);
        Assert.Contains("/config", cut.Markup);
    }

    [Fact]
    public async Task Analisis_RenderizaSinErrores_YMuestraPanelesBase()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        ctx.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost/") });
        ctx.Services.AddScoped<UiConfigService>();
        ctx.Services.AddScoped<UiSnapshotService>();

        ctx.Services.AddScoped<ExplorerStateService>();
        ctx.Services.AddScoped<IStatePersistenceService>(_ => new FakeStatePersistenceService());
        ctx.Services.AddScoped<IConfigService>(_ => new FakeConfigService());
        ctx.Services.AddScoped<IAuditoriaService, AuditoriaService>();
        ctx.Services.AddScoped<UiFeedbackService>();

        var cut = ctx.Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, builder =>
            {
                builder.OpenComponent<Analisis>(0);
                builder.CloseComponent();
            }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("ANALISIS DE IMBALANCES", cut.Markup);
            Assert.Contains("DISPONIBILIDAD DOCUMENTAL", cut.Markup);
            Assert.Contains("AUDITORIA DE RECIPROCIDAD", cut.Markup);
            Assert.Contains("PANEL DE DISCONFORMIDADES", cut.Markup);
        });
    }

    [Fact]
    public async Task Config_RenderizaSinErrores_YMuestraTitulo()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        ctx.Services.AddScoped<IConfigService>(_ => new FakeConfigService());
        ctx.Services.AddScoped<UiFeedbackService>();
        ctx.Services.AddScoped<ExplorerStateService>();
        ctx.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost/") });
        ctx.Services.AddScoped<FirebaseMotorsService>();
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        ctx.Services.AddScoped<UiConfigService>();
        ctx.Services.AddScoped<UiSnapshotService>();

        var cut = ctx.Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, builder =>
            {
                builder.OpenComponent<Config>(0);
                builder.CloseComponent();
            }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("CONFIGURACIÓN DEL MOTOR", cut.Markup);
        }, timeout: TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Informe_RenderizaSinErrores_YMuestraTitulo()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddMudServices();

        ctx.Services.AddScoped<ExplorerStateService>();
        ctx.Services.AddScoped<IStatePersistenceService>(_ => new FakeStatePersistenceService());
        ctx.Services.AddScoped<IConfigService>(_ => new FakeConfigService());
        ctx.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost/") });
        ctx.Services.AddScoped<UiConfigService>();
        ctx.Services.AddScoped<UiSnapshotService>();

        var cut = ctx.Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, builder =>
            {
                builder.OpenComponent<Informe>(0);
                builder.CloseComponent();
            }));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("INFORME DE IMBALANCES", cut.Markup);
        });
    }

    private sealed class FakeStatePersistenceService : IStatePersistenceService
    {
        public Task GuardarEstadoAsync(PersistenceState state) => Task.CompletedTask;

        public Task<PersistenceState?> CargarEstadoAsync() => Task.FromResult<PersistenceState?>(null);

        public Task LimpiarEstadoAsync() => Task.CompletedTask;
    }

    private sealed class FakeConfigService : IConfigService
    {
        public Task<ConfiguracionCore> CargarConfiguracionAsync()
        {
            var config = new ConfiguracionCore
            {
                Empresas = new List<EmpresaConfig>
                {
                    new EmpresaConfig { NombreEmpresa = "Empresa 1", NombreCarpeta = "EMP1" }
                }
            };

            return Task.FromResult(config);
        }

        public Task GuardarConfiguracionAsync(ConfiguracionCore config) => Task.CompletedTask;
    }
}
