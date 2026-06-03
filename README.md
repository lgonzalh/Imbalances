# Imbalances

<img width="1010" height="1029" alt="Imbalances Dashboard" src="https://lantonium.web.app/assets/img/imbalances/fotograma-00087.jpg" />

## English

Imbalances is a financial engineering solution developed in C# to optimize holding company auditing through high-speed processing of multiple Excel files. It ensures agile validation of reciprocity between Accounts Receivable (AR) and Accounts Payable (AP).

### Features

- High-speed batch processing across multiple Excel workbooks in distributed folders.
- Reciprocity auditing to identify financial imbalances between holding entities.
- Automated validation of critical report availability for monthly closing.
- Direct persistence over original templates without requiring external database infrastructure.
- Real-time discrepancies dashboard for prompt accounting decisions.

### Stack

- .NET 8 / ASP.NET Core
- C#
- Microsoft Excel Integration
- OneDrive API
- Web App (Cloud Deployed)

### Setup

Run the application:

```powershell
dotnet restore
dotnet build
dotnet run --launch-profile Imbalances
```

Open:

```text
http://localhost:5000/
```

## Espanol

Imbalances es una solucion de ingenieria financiera desarrollada en C# que optimiza la auditoria de holdings mediante el procesamiento acelerado de volumenes diversos de archivos Excel. Garantiza de forma agil la reciprocidad entre Cuentas por Cobrar (CxC) y Cuentas por Pagar (CxP).

### Funcionalidades

- Procesamiento masivo y rapido sobre multiples libros de Excel distribuidos en diversas subcarpetas.
- Auditoria de reciprocidad: Identificacion precisa de desequilibrios financieros entre entidades del holding.
- Validacion automatizada de disponibilidad documental para reportes criticos de cierre mensual.
- Persistencia directa en origen: Gestion de hallazgos realizada integramente sobre plantillas originales sin dependencia de base de datos externa.
- Panel de disconformidades en tiempo real para agilizar toma de decisiones y correcciones contables.

### Tecnologias

- .NET 8 / ASP.NET Core
- C#
- Integracion Microsoft Excel
- API de OneDrive
- Aplicacion Web (Despliegue en la Nube)

### Configuracion

Ejecuta la aplicacion:

```powershell
dotnet restore
dotnet build
dotnet run --launch-profile Imbalances
```

Abre:

```text
http://localhost:5000/
```
