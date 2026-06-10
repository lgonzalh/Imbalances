using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Imbalances.Core.Utils;
using Imbalances.Infrastructure.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

public class FSRRegressionTests
{
    private readonly ITestOutputHelper _output;

    public FSRRegressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TienePatronFsr_ExactFSR_ReturnsTrue()
    {
        Assert.True(Motor1Extractor.TienePatronFsr("FSR"));
        _output.WriteLine("✅ TienePatronFsr('FSR') = true");
    }

    [Fact]
    public void TienePatronFsr_ParenthesizedFSR_ReturnsTrue()
    {
        Assert.True(Motor1Extractor.TienePatronFsr("(FSR)"));
        _output.WriteLine("✅ TienePatronFsr('(FSR)') = true");
    }

    [Fact]
    public void TienePatronFsr_FundacionSolidRiver_ReturnsTrue()
    {
        Assert.True(Motor1Extractor.TienePatronFsr("FUNDACION SOLID RIVER"));
        _output.WriteLine("✅ TienePatronFsr('FUNDACION SOLID RIVER') = true");
    }

    [Fact]
    public void TienePatronFsr_FundacionSolidRiverAccented_ReturnsTrue()
    {
        Assert.True(Motor1Extractor.TienePatronFsr("FUNDACI\u00d3N SOLID RIVER"));
        _output.WriteLine("✅ TienePatronFsr('FUNDACIÓN SOLID RIVER') = true");
    }

    [Fact]
    public void TienePatronFsr_EmbeddedInPhrase_ReturnsTrue()
    {
        Assert.True(Motor1Extractor.TienePatronFsr("Aporte de accionista por pagar (FSR)"));
        _output.WriteLine("✅ TienePatronFsr('Aporte de accionista por pagar (FSR)') = true");
    }

    [Fact]
    public void TienePatronFsr_PrestamoFSR_ReturnsFalse()
    {
        Assert.False(Motor1Extractor.TienePatronFsr("Prestamo FSR"));
        _output.WriteLine("✅ TienePatronFsr('Prestamo FSR') = false (sin parentesis)");
    }

    [Fact]
    public void TienePatronFsr_NormalCompany_ReturnsFalse()
    {
        Assert.False(Motor1Extractor.TienePatronFsr("ALFA SA"));
        _output.WriteLine("✅ TienePatronFsr('ALFA SA') = false");
    }

    [Fact]
    public void TienePatronFsr_NullEmpty_ReturnsFalse()
    {
        Assert.False(Motor1Extractor.TienePatronFsr(null));
        Assert.False(Motor1Extractor.TienePatronFsr(""));
        Assert.False(Motor1Extractor.TienePatronFsr("   "));
        _output.WriteLine("✅ TienePatronFsr(null/empty) = false");
    }

    [Fact]
    public async Task FSR_EmbeddedInText_GeneraMovimiento()
    {
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" })
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
                [4] = new FakeRow(new() { ["C"] = "Aporte de accionista por pagar (FSR)", ["I"] = "1,000.50" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "1,000.50" })
            })
        });

        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" }
            },
            Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } },
            AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } }
        };

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06");

        Assert.Single(resultados);
        var mov = resultados[0];
        Assert.Equal("FUNDACION SOLID RIVER", mov.EmpresaContraparte);
        Assert.Equal(1000.50m, mov.Valor);
        _output.WriteLine($"✅ FSR embedded text generó movimiento: {mov.EmpresaOrigen} -> {mov.EmpresaContraparte} | {mov.Valor:N2}");
    }

    [Fact]
    public async Task FSR_EnRubro_NoGeneraMovimiento()
    {
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" })
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR (FSR)" })
            })
        });

        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" }
            },
            Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } },
            AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } }
        };

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06");

        Assert.Empty(resultados);
        _output.WriteLine("✅ FSR en rubro (sin valor) no genera movimiento");
    }

    [Fact]
    public async Task FSR_AporteAccionistaConDash_GeneraMovimiento()
    {
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" })
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
                [4] = new FakeRow(new() { ["C"] = "Aporte de accionista por pagar - (FSR)", ["I"] = "500.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "500.00" })
            })
        });

        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" }
            },
            Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } },
            AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } }
        };

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06");

        Assert.Single(resultados);
        Assert.Equal("FUNDACION SOLID RIVER", resultados[0].EmpresaContraparte);
        _output.WriteLine("✅ 'Aporte de accionista por pagar - (FSR)' -> FUNDACION SOLID RIVER");
    }

    [Fact]
    public async Task FSR_InversionPorPagarConAcento_GeneraMovimiento()
    {
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" })
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
                [4] = new FakeRow(new() { ["C"] = "Inversi\u00f3n por pagar (FSR)", ["I"] = "2,500.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "2,500.00" })
            })
        });

        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" }
            },
            Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } },
            AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } }
        };

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: "C:/data/ORIGEN/archivo.xlsx",
            fileStream: stream,
            config: config,
            periodo: "2026-06");

        Assert.Single(resultados);
        Assert.Equal("FUNDACION SOLID RIVER", resultados[0].EmpresaContraparte);
        _output.WriteLine("✅ 'Inversión por pagar (FSR)' -> FUNDACION SOLID RIVER");
    }

    [Fact]
    public async Task DosCorridasConsecutivas_ResultadosIdenticos()
    {
        var workbook = new FakeWorkbook(new IExcelWorksheet[]
        {
            new FakeWorksheet("BALANCE DE SITUACION", 50000, new()
            {
                [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }),
                [12] = new FakeRow(new() { ["C"] = "Cuentas por pagar proveedores", ["J"] = "6" })
            }),
            new FakeWorksheet("Nota 5", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }),
                [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.50" }),
                [5] = new FakeRow(new() { ["C"] = "FSR", ["I"] = "500.00" }),
                [6] = new FakeRow(new() { ["C"] = "Aporte de accionista por pagar (FSR)", ["I"] = "2,000.00" }),
                [7] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "3,500.50" })
            }),
            new FakeWorksheet("Nota 6", 50000, new()
            {
                [3] = new FakeRow(new() { ["C"] = "CUENTAS POR PAGAR" }),
                [4] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "750.00" }),
                [5] = new FakeRow(new() { ["C"] = "TOTAL CxP", ["I"] = "750.00" })
            })
        });

        var config = new ConfiguracionCore
        {
            Empresas = new()
            {
                new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" },
                new EmpresaConfig { NombreEmpresa = "ALFA SA" },
                new EmpresaConfig { NombreEmpresa = "BETA SA" },
                new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" }
            },
            Cuentas = new()
            {
                new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" },
                new CuentaConfig { NombreCuenta = "Cuentas por pagar proveedores", Tipo = "CxP" }
            },
            AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } }
        };

        var filePath = "C:/data/ORIGEN/archivo.xlsx";
        var periodo = "2026-06";

        async Task<List<Movimiento>> RunOnce()
        {
            var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
            await using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            return await motor1.ExtraerAsync(filePath, stream, config, periodo);
        }

        var r1 = await RunOnce();
        var r2 = await RunOnce();

        Assert.Equal(r1.Count, r2.Count);
        for (var i = 0; i < r1.Count; i++)
        {
            Assert.Equal(r1[i].EmpresaOrigen, r2[i].EmpresaOrigen);
            Assert.Equal(r1[i].EmpresaContraparte, r2[i].EmpresaContraparte);
            Assert.Equal(r1[i].Tipo, r2[i].Tipo);
            Assert.Equal(r1[i].Cuenta, r2[i].Cuenta);
            Assert.Equal(r1[i].Valor, r2[i].Valor);
            Assert.Equal(r1[i].Nota, r2[i].Nota);
            Assert.Equal(r1[i].Periodo, r2[i].Periodo);
        }

        _output.WriteLine($"✅ Dos corridas consecutivas produjeron {r1.Count} movimientos identicos cada una");
        foreach (var m in r1)
            _output.WriteLine($"   {m.EmpresaOrigen} -> {m.EmpresaContraparte} | {m.Tipo} | {m.Valor:N2}");
    }

    [Fact]
    public void FSR_Alias_ResolveCorrectly()
    {
        var empresaService = new EmpresaDetectionService();
        var empresasConfig = new List<EmpresaConfig>
        {
            new() { NombreEmpresa = "FUNDACION SOLID RIVER", NombreCarpeta = "FUNDACION SOLID RIVER" },
        };
        var aliasConfig = new List<EquivalenciaTercero>
        {
            new() { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" },
            new() { Alias = "Fundacion Solid River", NombreEmpresaDestino = "FUNDACION SOLID RIVER" },
        };

        var baseConfig = new ConfiguracionCore
        {
            Empresas = empresasConfig,
            AliasEmpresa = aliasConfig,
        };

        var empresaFSR = empresaService.DetectarEmpresa("FSR", baseConfig);
        Assert.NotNull(empresaFSR);
        Assert.Equal("FUNDACION SOLID RIVER", empresaFSR.NombreEmpresa);

        var empresaFundacion = empresaService.DetectarEmpresa("Fundacion Solid River", baseConfig);
        Assert.NotNull(empresaFundacion);
        Assert.Equal("FUNDACION SOLID RIVER", empresaFundacion.NombreEmpresa);

        _output.WriteLine("✅ FSR aliases resolve correctly to FUNDACION SOLID RIVER");
    }
}
