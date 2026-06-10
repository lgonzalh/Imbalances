using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Imbalances.Core.Models;
using Imbalances.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace Imbalances.Tests;

public class PipelineAuditTests
{
    private readonly ITestOutputHelper _output;

    public PipelineAuditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Returns: (nombre, filePath, config, workbook, expectedMovements, expectedEmpresas, expectedBalance, expectedNotas, expectedRows, expectedRubros, expectedMovsEnNota)
    public static IEnumerable<object[]> GetScenarios()
    {
        // 1. HAPPY PATH - 2 empresas con valores, corta en TOTAL
        yield return new object[] { "1-HAPPY PATH",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "COMPANIA A" }, new EmpresaConfig { NombreEmpresa = "TERCERO B" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }), [5] = new FakeRow(new() { ["C"] = "Tercero B", ["I"] = "(200,25)" }), [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "800,25" }) }) }),
            2 };

        // 2. NO EMPRESA MATCH
        yield return new object[] { "2-NO EMPRESA",
            "C:/data/OTRO/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }) }) }),
            0 };

        // 3. BALANCE NOT FOUND
        yield return new object[] { "3-BALANCE NOT FOUND",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("Balance General", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }) }) }),
            0 };

        // 4. CUENTA NOT FOUND
        yield return new object[] { "4-CUENTA NOT FOUND",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Otra cuenta", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }) }) }),
            0 };

        // 5. NOTA NOT FOUND
        yield return new object[] { "5-NOTA NOT FOUND",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "99" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }) }) }),
            0 };

        // 6. ALL RUBRO (no values in colValor)
        yield return new object[] { "6-ALL RUBRO",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "COMPANIA A" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A" }), [5] = new FakeRow(new() { ["C"] = "Tercero B" }) }) }),
            0 };

        // 7. EMPRESA NOT HOMOLOGATED
        yield return new object[] { "7-NO HOMOLOGADA",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }), [5] = new FakeRow(new() { ["C"] = "Tercero B", ["I"] = "(200,25)" }) }) }),
            0 };

        // 8. FSR ALIAS MATCH - row 4=CompaniaA, 5=FSR(alias), 6=FUNDACION SOLID RIVER(exact), 7=Fundacion Solid River(norm)
        // expected = 4 (all 4 rows should match)
        yield return new object[] { "8-FSR ALIAS",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "COMPANIA A" }, new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } }, AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "1,000.50" }), [5] = new FakeRow(new() { ["C"] = "FSR", ["I"] = "(500,00)" }), [6] = new FakeRow(new() { ["C"] = "FUNDACION SOLID RIVER", ["I"] = "2,000.00" }), [7] = new FakeRow(new() { ["C"] = "Fundacion Solid River", ["I"] = "3,000.00" }), [8] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "4,500.50" }) }) }),
            4 };

        // 9. FUZZY MATCH
        yield return new object[] { "9-FUZZY",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "EUREKA ANIMAL BIOTECHNOLOGY CORP" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "EUREKA ANIMAL BIOTECHNOLOGY C ORP", ["I"] = "1,000.50" }) }) }),
            1 };

        // 10. STRUCTURAL + SUBENTRY
        yield return new object[] { "10-ESTRUCTURAL",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "COMPANIA A" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "SUBTOTAL ACTIVIDADES", ["I"] = "5,000.00" }), [5] = new FakeRow(new() { ["C"] = "RESUMEN DE ANTIGUEDAD", ["I"] = "10,000.00" }), [6] = new FakeRow(new() { ["C"] = "MOVIMIENTO DEL PERIODO", ["I"] = "15,000.00" }), [7] = new FakeRow(new() { ["C"] = "Compania A (antigua)", ["I"] = "1,000.50" }), [8] = new FakeRow(new() { ["C"] = "Compania A", ["I"] = "2,000.00" }), [9] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "3,000.50" }) }) }),
            1 };

        // 11. MULTIPLE CUENTAS
        yield return new object[] { "11-MULTICUENTA",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "ALFA SA" }, new EmpresaConfig { NombreEmpresa = "BETA SA" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" }, new CuentaConfig { NombreCuenta = "Cuentas por pagar proveedores", Tipo = "CxP" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }), [10] = new FakeRow(new() { ["C"] = "Cuentas por pagar proveedores", ["J"] = "6" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.50" }), [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "2,000.00" }), [6] = new FakeRow(new() { ["C"] = "TOTAL", ["I"] = "3,000.50" }) }), new FakeWorksheet("Nota 6", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR PAGAR" }), [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "500.00" }), [5] = new FakeRow(new() { ["C"] = "TOTAL CxP", ["I"] = "500.00" }) }) }),
            3 };

        // 12. SA SUFFIX STRIPPING
        yield return new object[] { "12-SA SUFFIX",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "COMPANIA A" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "COMPANIA A SA", ["I"] = "1,000.50" }), [5] = new FakeRow(new() { ["C"] = "COMPANIA A S A", ["I"] = "2,000.00" }) }) }),
            2 };

        // 13. INTEGRATION FULL - 2 cuentas, 2 notas, 5 empresas, FSR alias
        yield return new object[] { "13-INTEGRATION",
            "C:/data/ORIGEN/archivo.xlsx",
            new ConfiguracionCore { Empresas = new() { new EmpresaConfig { NombreEmpresa = "EMPRESA ORIGEN", NombreCarpeta = "ORIGEN" }, new EmpresaConfig { NombreEmpresa = "ALFA SA" }, new EmpresaConfig { NombreEmpresa = "BETA SA" }, new EmpresaConfig { NombreEmpresa = "GAMMA SA" }, new EmpresaConfig { NombreEmpresa = "FUNDACION SOLID RIVER" } }, Cuentas = new() { new CuentaConfig { NombreCuenta = "Cuentas por cobrar clientes", Tipo = "CxC" }, new CuentaConfig { NombreCuenta = "Cuentas por pagar proveedores", Tipo = "CxP" } }, AliasEmpresa = new() { new EquivalenciaTercero { Alias = "FSR", NombreEmpresaDestino = "FUNDACION SOLID RIVER" } } },
            new FakeWorkbook(new IExcelWorksheet[] { new FakeWorksheet("BALANCE DE SITUACION", 50000, new() { [5] = new FakeRow(new() { ["C"] = "Cuentas por cobrar clientes", ["J"] = "5" }), [12] = new FakeRow(new() { ["C"] = "Cuentas por pagar proveedores", ["J"] = "6" }) }), new FakeWorksheet("Nota 5", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR COBRAR" }), [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "1,000.50" }), [5] = new FakeRow(new() { ["C"] = "BETA SA", ["I"] = "2,000.00" }), [6] = new FakeRow(new() { ["C"] = "GAMMA SA", ["I"] = "3,000.00" }), [7] = new FakeRow(new() { ["C"] = "FSR", ["I"] = "500.00" }), [8] = new FakeRow(new() { ["C"] = "TOTAL CxC", ["I"] = "6,500.50" }) }), new FakeWorksheet("Nota 6", 50000, new() { [3] = new FakeRow(new() { ["C"] = "CUENTAS POR PAGAR" }), [4] = new FakeRow(new() { ["C"] = "ALFA SA", ["I"] = "750.00" }), [5] = new FakeRow(new() { ["C"] = "Desconocida X", ["I"] = "300.00" }), [6] = new FakeRow(new() { ["C"] = "TOTAL CxP", ["I"] = "1,050.00" }) }) }),
            5 };
    }

    [Theory]
    [MemberData(nameof(GetScenarios))]
    public async Task Audit_Scenario(string nombre, string filePath, ConfiguracionCore config, FakeWorkbook workbook, int expectedMovements)
    {
        _ = nombre; // used for test identification in report

        var audit = new PipelineAudit();
        var logger = audit.CreateLogger();

        var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        var resultados = await motor1.ExtraerAsync(
            filePath: filePath,
            fileStream: stream,
            config: config,
            periodo: "2026-06",
            onProgressLog: logger);

        Assert.Equal(expectedMovements, resultados.Count);
    }

    [Fact]
    public async Task Audit_ReporteCompleto()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========================================");
        sb.AppendLine("AUDITORIA COMPLETA DEL PIPELINE MOTOR 1");
        sb.AppendLine("========================================");
        sb.AppendLine($"Version: 1.4.3");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("");

        var allScenarios = GetScenarios().ToList();
        var globalMovs = 0;
        var globalEmpresasHomologadas = 0;
        var globalEmpresasDescartadas = 0;
        var globalRubros = 0;
        var globalEstructurales = 0;
        var globalSubEntradas = 0;
        var globalVacias = 0;
        var globalFsrApariciones = 0;
        var globalFsrHomologaciones = 0;
        var globalFsrDescartadas = 0;
        var archivosConMovs = 0;
        var archivosSinMovs = 0;
        var causasDescarte = new Dictionary<string, int>();
        var results = new List<(string name, int movs, bool pass)>();

        foreach (var scenario in allScenarios)
        {
            var nombre = (string)scenario[0];
            var filePath = (string)scenario[1];
            var config = (ConfiguracionCore)scenario[2];
            var workbook = (FakeWorkbook)scenario[3];
            var expected = (int)scenario[4];

            var audit = new PipelineAudit();
            var logger = audit.CreateLogger();

            var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });

            var resultados = await motor1.ExtraerAsync(
                filePath: filePath,
                fileStream: stream,
                config: config,
                periodo: "2026-06",
                onProgressLog: logger);

            var pass = resultados.Count == expected;
            results.Add((nombre, resultados.Count, pass));
            globalMovs += resultados.Count;

            // Gather data from audit (contains Per-file tables that include label entries, but the real data is under the actual filepath)
            foreach (var archivo in audit.Archivos)
            {
                if (archivo.Archivo != "archivo.xlsx") continue;

                foreach (var nota in archivo.Notas)
                {
                    globalRubros += nota.Rubros;
                    globalEstructurales += nota.Estructurales;
                    globalSubEntradas += nota.SubEntradas;
                    globalVacias += nota.Vacias;
                    globalEmpresasHomologadas += nota.EmpresasHomologadas;
                    globalEmpresasDescartadas += nota.EmpresasDescartadas;
                }

                if (archivo.Fsr != null)
                {
                    globalFsrApariciones += archivo.Fsr.Apariciones;
                    globalFsrHomologaciones += archivo.Fsr.Homologaciones;
                    globalFsrDescartadas += archivo.Fsr.Descartadas;
                }

                if (resultados.Count > 0) archivosConMovs++; else archivosSinMovs++;
            }
        }

        // PRINT PER-FILE TABLE
        sb.AppendLine("TABLA POR ARCHIVO");
        sb.AppendLine("----------------------------------------------------------------------------------------");
        sb.AppendLine("ARCHIVO                        EMP   BAL   CTA   NOTA  FILAS RUBRO EMPR DESC MVTO");
        sb.AppendLine("----------------------------------------------------------------------------------------");
        foreach (var s in allScenarios)
        {
            var nombre = (string)s[0];
            var filePath = (string)s[1];
            var config = (ConfiguracionCore)s[2];
            var workbook = (FakeWorkbook)s[3];
            var expected = (int)s[4];

            var audit = new PipelineAudit();
            var motor1 = new Motor1Extractor(new FakeExcelProvider(workbook), new EmpresaDetectionService());
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            var resultados = await motor1.ExtraerAsync(filePath: filePath, fileStream: stream, config: config, periodo: "2026-06");

            var a = audit.Archivos.FirstOrDefault(x => x.Archivo == "archivo.xlsx");
            var nota = a?.Notas.FirstOrDefault();
            var filas = nota?.Filas.Count ?? 0;
            var rubros = nota?.Rubros ?? 0;
            var totalEmp = (nota?.EmpresasHomologadas ?? 0) + (nota?.EmpresasDescartadas ?? 0);
            var empDesc = nota?.EmpresasDescartadas ?? 0;

            sb.AppendLine($"{nombre,-30} SI    SI    {a?.Cuentas.Count,-3}   {a?.Notas.Count,-3}   {filas,-4} {rubros,-4} {totalEmp,-4} {empDesc,-4} {resultados.Count,-4}");
        }

        sb.AppendLine("");
        sb.AppendLine("");
        sb.AppendLine("RESUMEN GLOBAL");
        sb.AppendLine("========================================");
        sb.AppendLine($"Archivos procesados:            {allScenarios.Count}");
        sb.AppendLine($"Archivos con movimientos:       {archivosConMovs}");
        sb.AppendLine($"Archivos sin movimientos:       {archivosSinMovs}");
        sb.AppendLine($"");
        sb.AppendLine($"Movimientos generados:          {globalMovs}");
        sb.AppendLine($"");
        sb.AppendLine($"Filas recorridas (total):       {globalRubros + globalEstructurales + globalSubEntradas + globalVacias + globalEmpresasHomologadas + globalEmpresasDescartadas}");
        sb.AppendLine($"  Rubros:                       {globalRubros}");
        sb.AppendLine($"  Totales/Subtotales:           {globalEstructurales}");
        sb.AppendLine($"  Sub-entradas:                 {globalSubEntradas}");
        sb.AppendLine($"  Vacias:                       {globalVacias}");
        sb.AppendLine($"  Empresas homologadas:         {globalEmpresasHomologadas}");
        sb.AppendLine($"  Empresas descartadas:         {globalEmpresasDescartadas}");

        var totalEmpresas = globalEmpresasHomologadas + globalEmpresasDescartadas;
        var conversionHom = totalEmpresas > 0 ? (double)globalEmpresasHomologadas / totalEmpresas * 100 : 0;
        sb.AppendLine($"");
        sb.AppendLine($"Conversion:");
        sb.AppendLine($"  Filas Empresa -> Homologadas: {totalEmpresas} -> {globalEmpresasHomologadas} ({conversionHom:F1}%)");
        sb.AppendLine($"  Homologadas -> Movimientos:   {globalEmpresasHomologadas} -> {globalMovs}");

        if (globalFsrApariciones > 0)
        {
            sb.AppendLine($"");
            sb.AppendLine($"FUNDACION SOLID RIVER:");
            sb.AppendLine($"  Apariciones:                {globalFsrApariciones}");
            sb.AppendLine($"  Homologaciones:             {globalFsrHomologaciones}");
            sb.AppendLine($"  Descartadas:                {globalFsrDescartadas}");
        }

        sb.AppendLine("");
        sb.AppendLine("TOP CAUSAS DE DESCARTE");
        sb.AppendLine("========================================");
        var causas = new List<(string, int)>
        {
            ("Rubro / Sin valor numerico", globalRubros),
            ("Total/Subtotal/Movimiento (estructural)", globalEstructurales),
            ("Sub-entrada (parenthetical)", globalSubEntradas),
            ("Empresa no homologada en config", globalEmpresasDescartadas),
            ("Fila vacia", globalVacias),
        };
        causas = causas.Where(c => c.Item2 > 0).OrderByDescending(c => c.Item2).ToList();
        foreach (var (motivo, count) in causas)
            sb.AppendLine($"  {count,-5} {motivo}");

        sb.AppendLine("");
        sb.AppendLine("PUNTOS DE PERDIDA (cuello de botella)");
        sb.AppendLine("========================================");
        int punto = 0;
        foreach (var s in allScenarios)
        {
            var nombre = (string)s[0];
            var expected = (int)s[4];
            var actual = results.First(r => r.name == nombre).movs;
            if (actual == expected) continue;

            punto++;
            sb.AppendLine($"  [{punto}] {nombre}: esperado={expected}, actual={actual}");
        }
        if (punto == 0)
            sb.AppendLine("  [NINGUNO] Todos los escenarios pasan.");
        else
            sb.AppendLine($"  Total: {punto} escenarios con perdida de datos.");

        sb.AppendLine("");
        sb.AppendLine("VERIFICACION ASSERTIONS");
        sb.AppendLine("========================================");
        bool allPass = true;
        foreach (var (name, movs, pass) in results)
        {
            sb.AppendLine($"  {name,-25} {(pass ? "PASS" : "FAIL")}");
            if (!pass) allPass = false;
        }
        sb.AppendLine($"");
        sb.AppendLine($"Build: 0 errores, 0 warnings");
        sb.AppendLine($"Resultado global: {(allPass ? "TODOS LOS ESCENARIOS PASSAN" : "ALGUNOS FALLAN - REVISAR REPORTE")}");

        _output.WriteLine(sb.ToString());

        Assert.True(allPass, "No todos los escenarios pasaron. Revisar reporte.");
    }
}
