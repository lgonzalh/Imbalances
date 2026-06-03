using System;
using System.Text.Json;
using Imbalances.Core.Models;

// Quick test to verify JSON serialization/deserialization
var config = new ConfiguracionCore
{
    Empresas = new()
    {
        new EmpresaConfig 
        { 
            Nombre = "TEST EMPRESA", 
            NombreCarpeta = "TEST_CARPETA",
            CarpetaRegex = ".*",
            ArchivoRegex = ".*",
            HojaBalance = "Balance"
        }
    },
    Cuentas = new()
    {
        new CuentaConfig
        {
            NombreCuenta = "Test Cuenta",
            Tipo = "CxC",
            ColumnaValor = "C",
            ColumnaNota = "J"
        }
    }
};

Console.WriteLine("=== SERIALIZATION TEST ===\n");

// Serialize
var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine("Serialized JSON:\n");
Console.WriteLine(json);

// Check for camelCase keys
bool hasCamelCaseKeys = json.Contains("\"nombreEmpresa\"") 
    && json.Contains("\"nombreCarpeta\"")
    && json.Contains("\"nombreCuenta\"")
    && json.Contains("\"tipo\"")
    && json.Contains("\"columnaValor\"");

Console.WriteLine($"\n✓ Contains camelCase keys: {hasCamelCaseKeys}");

// Deserialize back
var deserialized = JsonSerializer.Deserialize<ConfiguracionCore>(json);

Console.WriteLine("\n=== DESERIALIZATION TEST ===\n");
Console.WriteLine($"✓ Empresas count: {deserialized?.Empresas.Count}");
Console.WriteLine($"✓ First empresa NombreEmpresa: {deserialized?.Empresas[0]?.Nombre}");
Console.WriteLine($"✓ First empresa NombreCarpeta: {deserialized?.Empresas[0]?.NombreCarpeta}");
Console.WriteLine($"✓ Cuentas count: {deserialized?.Cuentas.Count}");
Console.WriteLine($"✓ First cuenta NombreCuenta: {deserialized?.Cuentas[0]?.NombreCuenta}");
Console.WriteLine($"✓ First cuenta Tipo: {deserialized?.Cuentas[0]?.Tipo}");

bool success = deserialized?.Empresas[0]?.Nombre == "TEST EMPRESA" 
    && deserialized?.Empresas[0]?.NombreCarpeta == "TEST_CARPETA"
    && deserialized?.Cuentas[0]?.NombreCuenta == "Test Cuenta";

Console.WriteLine($"\n{'✓'} Round-trip serialization/deserialization: {(success ? "SUCCESS" : "FAILED")}");
