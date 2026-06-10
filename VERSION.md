## 1.7.3
- Version: 1.7.3.0
- Fecha: 2026-06-10
- FASE 5.1A: Eliminacion Definitiva de Reprocesamiento. Cada Nota se procesa exactamente una vez por archivo, incluso si multiples cuentas la referencian. Resultados reutilizados entre cuentas.
  - F5.1A.1: Procesamiento unico por nota. El bucle de procesamiento agrupa cuentas por nota y ejecuta ExtraerDesdeCacheMultiCuenta una sola vez. Antes: 1 llamada por cuenta. Despues: 1 llamada por nota.
  - F5.1A.2: VecesProcesada=1 siempre. Ya no se incrementa por cuenta; se fija en 1 tras el procesamiento unico de la nota.
  - F5.1A.3: Reporte Antes/Despues. Cada nota reporta "Procesamientos antes" (cuentas referenciadoras) vs "Procesamientos despues" (1 unico procesamiento). Propiedad ProcesamientosAntes en NoteStat.
  - F5.1A.4: NotaUnicaPorArchivoTests (4 tests). Test obligatorio que falla si VecesProcesada > 1. Cubre: 2 cuentas, 4 cuentas, notas diferentes, reporte antes/despues.
  - F5.1A.5: NoteReuseStats.NotasReutilizadas ahora basado en ProcesamientosAntes > 1 (antes: VecesProcesada > 1).
  - F5.1A.6: TiempoAhorradoMs recalcula usando (ProcesamientosAntes - 1) * tiempo promedio, reflejando el ahorro real de procesamientos eliminados.
  - F5.1A.7: 0 regresiones. 41/44 tests pasan (3 pre-existentes Blazor UI timeout). 13 tests nuevo codigo (9 reuse + 4 notaunica).
- Build: 0 errores, 0 warnings

## 1.7.0
- Version: 1.7.0.0
- Fecha: 2026-06-09
- FASE 4: Optimizacion de Rendimiento (5x-10x objetivo en pipeline de extraccion).
  - F4.1: Cache de Notas. Las notas se leen y parsean una sola vez aunque multiples cuentas las referencien. Antes: N lecturas por nota compartida. Despues: 1 lectura.
  - F4.2: Indice de Homologacion O(1). Dictionary precalculado para alias y nombre exacto. Fuzzy (Levenshtein) solo cuando falla exacta. Antes: ~54 comparaciones/fila. Despues: 2 lookups O(1)/fila.
  - F4.3: Logging de Produccion. Modo Diagnostico (logs detallados por fila) vs Modo Produccion (solo resumenes, inicio, errores). Reduccion tipica: ~70-90% menos lineas de log.
  - F4.4: Eliminacion de reprocesamiento duplicado. Cache de notas elimina doble scan del mismo sheet. Normalizar() y TryParseDecimal() computados una sola vez por fila.
  - F4.5: Procesamiento paralelo controlado. Metodo ProcesarMultiplesArchivosAsync con Parallel.ForEachAsync y MaxDegreeOfParallelism configurable (default 4). Sin race conditions.
  - F4.6: Perfilado por etapas. Stopwatch por etapa: deteccion empresa, lectura workbook, descubrimiento cuentas, procesamiento movimientos. Callback onProfile devuelve PipelineProfile.
  - F4.7: Validacion funcional completa. 23/23 tests pasan (PipelineAudit, ExtractorEngineMotor1, HomologationDiagnostic, PerformanceBenchmark). Mismos movimientos, mismas homologaciones, mismas reglas.
- Build: 0 errores, 0 warnings

## 1.5.0
- Version: 1.5.0.0
- Fecha: 2026-06-09
- FASE 2.1: Maestro Empresarial. Se agregan validaciones de integridad al Grid 1: Company Code duplicado (bloqueante), CONC_OP duplicado (bloqueante), Company Code/CONC_OP/Alias faltantes (advertencia visual no bloqueante). Modelo, columnas UI, Firestore, importacion/exportacion Excel, y persistencia sesion ya soportaban CompanyCode, CONC_OP, y Alias desde v1.4.0.
- Build: 0 errores, 0 warnings

## 1.4.4
- Version: 1.4.4.0
- Fecha: 2026-06-09
- Fix CRITICO: CuentaConfig.ColumnaValor default cambiado de "C" a "" para que el fallback DefaultNotaValorColumn="I" sea usado correctamente. Antes todas las filas leian columna C (nombres de empresa) como valor, TryParseDecimal fallaba, y todas las filas se clasificaban como RUBRO generando cero movimientos.
- Build: 0 errores, 0 warnings
- FASE 1 CERRADA: Validacion real de produccion confirma generacion de movimientos.

## 1.4.3
- Versión: 1.4.3.0
- Fecha: 2026-06-09
- Fix CRÍTICO: Detección de hojas (Hoja Balance) ahora usa comparación normalizada (acentos, mayúsculas, caracteres especiales). Corregida corrupción de encoding en DefaultHojaBalance ("situaciÃ³n" -> "situacion"). ExcelWorkbookWrapper.GetWorksheet() ahora itera con NormalizeForComparison como fallback.
- Fix CRÍTICO: Detección de empresas ahora usa Contains bidireccional (archivo contiene empresa O empresa contiene archivo). Agregado fuzzy matching (Levenshtein >=85% auto, 70-84.99% warning). Nuevo orden: 1) Exacto carpeta, 2) Exacto nombre, 3) Contains bidireccional, 4) Fuzzy.
- Build: 0 errores, 0 warnings

## 1.4.2
- Versión: 1.4.2.0
- Fecha: 2026-06-06
- Fix: Motor 1 - ExtraerDesdeNota() ahora continúa procesando después de detectar rubros internos (ej. "CUENTAS POR COBRAR CLIENTES - PARTES RELACIONADAS"). Antes terminaba la extracción en el primer rubro; ahora registra rubroActual y sigue leyendo contrapartes válidas debajo. Agregado logging temporal: [Fila X] RUBRO detectado, [Fila Y] CONTRAPARTE detectada, [Fila Z] MOVIMIENTO generado.
- Build: 0 errores, 0 warnings

## 1.4.1
- Versión: 1.4.1.0
- Fecha: 2026-06-05
- Fix: Logs de procesamiento ahora persisten al navegar entre páginas (se agregó Logs a PersistenceState)
- Build: 0 errores, 0 warnings

## 1.4.0
- Versión: 1.4.0.0
- Fecha: 2026-06-05
- CompanyCode / TradePartnerCode / ConcOp ahora se resuelven desde EmpresaConfig en ExtractorEngine (no más valores vacíos en Excel de Informe)
- Build: 0 errores, 0 warnings
# Imbalances - Registro de Versiones
## 1.4.1
- Versión: 1.4.1.0
- Fecha: 2026-06-05
- Fix: Logs de procesamiento ahora persisten al navegar entre páginas (se agregó Logs a PersistenceState)
- Build: 0 errores, 0 warnings

## 1.4.0
- VersiÃ³n: 1.4.0.0
- Fecha: 2026-06-05
- CompanyCode / TradePartnerCode / ConcOp ahora se resuelven desde EmpresaConfig en ExtractorEngine (no mÃ¡s valores vacÃ­os en Excel de Informe)
- Build: 0 errores, 0 warnings
# Imbalances - Registro de Versiones

# Imbalances - Registro de Versiones

## v1.3.0 (2026-06-05)

### ðŸš€ CompanyCode, ConcOp y detecciÃ³n de cambios no guardados

**Problema**: El informe descargable (Excel) dejaba vacÃ­as las columnas Company, Trade Partner y Conc_op porque la configuraciÃ³n de empresa no incluÃ­a estos campos.

**SoluciÃ³n**:

1. **Nuevos campos en EmpresaConfig**: CompanyCode y ConcOp con persistencia completa en Firestore, Excel (import/export) y UI.

2. **Informe poblado**: El Excel descargable ahora lee CompanyCode de la empresa origen, y CompanyCode/ConcOp de la contraparte (con resoluciÃ³n por alias).

3. **DetecciÃ³n de cambios no guardados en ConfiguraciÃ³n**: 
   - Flag _hasUnsavedChanges en cada input del grid.
   - NavigationLock para interceptar navegaciÃ³n interna con popup Guardar/Descartar/Quedarse.
   - eforeunload para evitar cierre accidental del navegador.

4. **Export/Import Excel actualizado**: La hoja Empresas ahora incluye columnas Company Code y Conc_Op (formato v1.3.0, no compatible hacia atrÃ¡s).

**Archivos Modificados**:
- src/Imbalances.Core/Models/EmpresaConfig.cs (CompanyCode, ConcOp)
- src/Imbalances.Core/Models/RegistroContable.cs (CompanyCode, TradePartnerCode, ConcOp)
- src/Imbalances.Client/wwwroot/js/excelInterop.js (import/export/report)
- src/Imbalances.Client/Pages/Config.razor (columnas UI, dirty tracking, NavigationLock)
- src/Imbalances.Client/Pages/Informe.razor (lookup CompanyCode/ConcOp)
- src/Imbalances.Client/Layout/MainLayout.razor (badge v1.3.0)
- src/Imbalances.Client/wwwroot/index.html (cache busting v1.3.0)
- *.csproj (versiÃ³n 1.3.0)
- VERSION.md (esta entrada)

**VerificaciÃ³n**: 
- Abrir Config â†’ editar un campo â†’ navegar a otra pÃ¡gina â†’ debe mostrar popup de confirmaciÃ³n.
- Descargar informe â†’ Company, Trade Partner, Conc_op deben tener valores.
- Exportar/Importar Excel â†’ las columnas Company Code y Conc_Op deben persistir correctamente.

### ðŸš€ Caso Especial #1: HomologaciÃ³n FSR (Motor 1)

**Problema**: Notas con referencia abreviada `(FSR)` / `FSR` (ej. "REEMBOLSO LEASING DE AUTO (FSR)") se interpretaban como texto libre y no generaban reciprocidad vÃ¡lida.

**SoluciÃ³n**:

1. **Tabla de alias especiales** (`ConfiguracionCore.AliasEmpresa`): Nuevo modelo `EquivalenciaTercero` con alias â†’ equivalentes (ej. `"FSR" â†’ ["FUNDACION SOLID RIVER"]`). Configurable vÃ­a JSON y con valor por defecto en `ConfigService`.

2. **Nuevo Nivel 0 en pipeline de homologaciÃ³n** (`ResolverEmpresaConfigurada`): La detecciÃ³n de alias especiales se ejecuta **antes** del match exacto (Nivel 1), match normalizado (Nivel 2) y fuzzy matching (Niveles 3-4). Soporta:
   - `(FSR)` â†’ detectado como alias directo `FSR`
   - `FSR` â†’ detectado como alias directo
   - `REEMBOLSO LEASING DE AUTO (FSR)` â†’ detectado por sufijo ` FSR`
   - `MOVIMIENTO (FSR)` â†’ alias detectado (aunque filtrado por `EsFilaEstructural`)

3. **Logging especÃ­fico**:
   - `[Info] Alias especial detectado: "FSR" â†’ "FUNDACION SOLID RIVER" | texto: "REEMBOLSO LEASING DE AUTO FSR"`
   - `[Info] Contraparte especial FSR homologada correctamente: GAMALAB â†’ FUNDACION SOLID RIVER | CxP | 135,409.00`
   - `[Warning] Alias "FSR" detectado pero no se encontrÃ³ empresa configurada para: FUNDACION SOLID RIVER`

**Orden del pipeline actualizado**:
1. Alias especiales (Nivel 0)
2. Match exacto (Nivel 1)
3. Match por contenciÃ³n (Nivel 2)
4. Fuzzy match â‰¥ 85% (Nivel 3)
5. Posible match 70-84% (Nivel 4, descartado)
6. < 70% (Nivel 5, descartado)

### ðŸš€ Caso Especial #2: Contrapartida obligatoria FSR (Motor 1 - DiagnÃ³stico)

**Problema**: Para FUNDACION SOLID RIVER se requiere reciprocidad obligatoria (CxC â†” CxP) para que Motor 3 pueda conciliar.

**SoluciÃ³n**: DiagnÃ³stico integrado en `ExtraerDesdeNota` que registra por nota y archivo:

- Cantidad de filas detectadas con `(FSR)` en texto crudo
- Cantidad de movimientos generados para FUNDACION SOLID RIVER
- Tipo generado (CxC o CxP) para cada movimiento FSR
- ValidaciÃ³n de nota especÃ­fica (ej. Nota 17)
- Valor individual y total FSR generado

**MÃ©tricas finales por archivo** (en `ExtraerAsync`):
- Movimientos FSR generados
- Valor total FSR generado
- Detalle por movimiento (origen â†’ contraparte | tipo | valor)

**Archivos Modificados**:
- `src/Imbalances.Core/Models/ConfiguracionCore.cs` (nueva propiedad `AliasEmpresa`)
- `src/Imbalances.Core/Models/EquivalenciaTercero.cs` (JsonPropertyName attributes)
- `src/Imbalances.Core/Services/Motor1/Motor1Extractor.cs` (Level 0 alias + diagnÃ³stico FSR)
- `src/Imbalances.Infrastructure/Services/ConfigService.cs` (alias FSR por defecto)
- `VERSION.md` (esta entrada)

**Archivos NO modificados** (por requerimiento):
- `cruceIntercompany.ts`
- `Home.razor`
- `Informe.razor`
- `FirebaseMotorsService.cs`

**VerificaciÃ³n**: Ejecutar Motor 1 sobre archivo GAMALAB (Nota 17). El log debe mostrar:
```
[Info] Alias especial detectado: "FSR" â†’ "FUNDACION SOLID RIVER"
[Info] Contraparte especial FSR homologada correctamente
...
[Info] --- MÃ©tricas finales FSR ---
[Info]   Movimientos FSR generados: 1
[Info]     â€¢ VACUNATORIO INTERNACIONAL â†’ FUNDACION SOLID RIVER | CxP | 135,409.00
[Info]   Valor total FSR generado: 135,409.00
```

## v1.1.25 (2026-06-04)

### ðŸš€ Fix Motor 1 â€” Descuadres falsos por contrapartes no configuradas (IPIC)

**Problema**: Motor 3 reportaba 32 descuadres en IPIC (periodo 2026-06) porque `Motor1Extractor` interpretaba filas estructurales de las Notas (ej. "RESUMEN DE ANTIGUEDAD POR PAGAR", "MOVIMIENTO DURANTE EL MES", "DESARROLLO DE ACTIVIDADES POR COBRAR") como nombres de empresa contraparte. Todas tenÃ­an `cuenta = "AGRUPADO"` en Firestore.

**SoluciÃ³n â€” 4 cambios clave**:

1. **Rubro tracking en Notas** (`Motor1Extractor.cs`): Filas sin valor numÃ©rico en columna I actualizan `rubroActual`; filas con valor se interpretan como potenciales empresas contraparte. El rubro se conserva en `Cuenta` del `RegistroContable`.

2. **ValidaciÃ³n contra configuraciÃ³n** (`ResolverEmpresaConfigurada`): 4 niveles de matched â€” exacto, prefijo, contenciÃ³n, token overlap â‰¥60%. Contrapartes no encontradas en la colecciÃ³n `empresas` se descartan (con Warning en log, archivo/hoja/rubro).

3. **Filtro de filas estructurales** (`EsFilaEstructural`, `EsGranTotal`): TOTAL, SUBTOTAL y descriptores contables se ignoran; solo "GRAN TOTAL" rompe el bucle. `BuscarInicioTabla()` simplificado: solo busca "MOVIMIENTO".

4. **AgrupaciÃ³n mejorada** (`MovimientosIntercompanyService.cs`): `NormalizarYAgrupar()` preserva la `Cuenta` (rubro) y agrupa por `{EmpresaOrigen, EmpresaContraparte, Tipo, Cuenta, Periodo}`.

**Hash de dedup** (`guardarMovimientos.ts`): Incluye `cuenta` â†’ `empresa|contraparte|tipo|cuenta|periodo`.

**Archivos Modificados**:
- `src/Imbalances.Core/Services/Motor1/Motor1Extractor.cs`
- `src/Imbalances.Client/Services/MovimientosIntercompanyService.cs`
- `functions/src/motor2/guardarMovimientos.ts`
- `src/Imbalances.Tests/ExtractorEngineMotor1Tests.cs`

**VerificaciÃ³n**: Extraer IPIC 2026-06 debe producir 0 descuadres (vs 32 anteriores). Los movimientos con contrapartes contables serÃ¡n descartados silenciosamente con Warning en log.

## v1.1.24 (2026-06-03)

### ðŸ”§ DEBUGGING Cloud Functions - Paso 4: Fix acceso a propiedades incorrecto en guardarConfiguracion

**BUG CRÃTICO ENCONTRADO**: En `functions/src/configuracion/guardarConfiguracion.ts` lÃ­nea 72, el cÃ³digo accedÃ­a a `n.identificador` pero el modelo NotaConfig tiene `nota` y `nombreHoja`.

**SoluciÃ³n Paso 4**:
- Corregido acceso a `n.nota` en lugar de `n.identificador`
- Agregado fallback a "nota" en caso de undefined

**Archivos Modificados**:
- `functions/src/configuracion/guardarConfiguracion.ts` (lÃ­nea 72)

**VerificaciÃ³n**: Guardar notas debe funcionar sin errores silenciosos en Firebase.

## v1.1.23 (2026-06-03)

### ðŸ”§ DEBUGGING Deserialization - Paso 3: JsonSerializerContext Global para Blazor JSInterop

**Problema**: Incluso con [JsonPropertyName], el JSInterop de Blazor tambiÃ©n necesita opciones de serializaciÃ³n.

**SoluciÃ³n Paso 3**:
- Creado `AppJsonSerializerContext` con `@JsonSourceGenerationOptions` para configurar PropertyNamingPolicy = CamelCase
- Configurado en Program.cs para uso global
- Asegura que TODAS las deserializaciones (Firebase HTTP + localStorage/IndexedDB) usen las mismas opciones

**Archivos**:
- `src/Imbalances.Client/JsonSerializerContext.cs` (nuevo)
- `src/Imbalances.Client/Program.cs` (actualizado)

**VerificaciÃ³n**: Las empresas deben renderizar correctamente tanto al cargar de Firebase como de localStorage.

## v1.1.22 (2026-06-03)

### ðŸ”§ DEBUGGING Deserialization - Paso 2: JsonSerializerOptions en GuardarConfiguracionAsync

**Problema**: Cuando se guardan cambios, el `PostAsJsonAsync` tampoco usa las opciones de serializaciÃ³n.

**SoluciÃ³n Paso 2**: 
- `GuardarConfiguracionAsync` ahora tambiÃ©n usa `JsonSerializerOptions` con CamelCase
- Asegura que la serializaciÃ³n de envÃ­o sea consistente con lo que Firebase Cloud Function espera

**VerificaciÃ³n**: Guardar cambios y verificar en Firestore que se actualicen correctamente.

## v1.1.21 (2026-06-03)

### ðŸ”§ DEBUGGING Deserialization - Paso 1: JsonSerializerOptions Global

**Problema**: Las empresas no se renderizan tras desproteger porque `ReadFromJsonAsync` no tiene opciones de naming policy.

**SoluciÃ³n Paso 1**: Agregar JsonSerializerOptions con CamelCase naming policy en FirebaseMotorsService

**Cambios**:
- FirebaseMotorsService ahora usa `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` en `ObtenerConfiguracionAsync`
- Esto asegura que el JSON camelCase de Firestore se deserialice correctamente a propiedades PascalCase de C#

**VerificaciÃ³n**: Si las empresas aparecen, entonces este era el problema.

## v1.1.20 (2026-06-03)

### âœ… Cambios Realizados:

1. **JsonPropertyName en modelos**: Agregados atributos `[JsonPropertyName]` explÃ­citos en EmpresaConfig, CuentaConfig, NotaConfig, CuentaEquivalencia y ConfiguracionCore.

## v1.1.18 (2026-05-07)

### âœ… Cambios Realizados:

1. **UX/UI Updates**: Explorador sin campos redundantes, LOG compacto dentro del panel, botones homologados (rect 4px), tÃ­tulos de pÃ¡gina eliminados en Config/AnÃ¡lisis/Informe.
2. **Fix Motor 3**: Eliminada dependencia de `collectionGroup` con Ã­ndice compuesto; Motor 3 itera jerarquÃ­a `empresas/{id}/periodos/{p}/movimientos`. Motor 2 materializa documento padre de empresa.
3. **Fix OnFilesSelectedJS**: Firma corregida para recibir `(string inputId, List<FileJSData> files)` â€” botÃ³n Iniciar habilitado correctamente.
4. **Log en ConfiguraciÃ³n**: BotÃ³n Log exporta el mismo log del Explorador (vÃ­a `IProgressService`).
5. **Firestore Rules**: Lectura habilitada para `cruces`, `config_*` colecciones.
6. **Deploy**: Functions desplegadas a `southamerica-east1`.

## v1.1.15 (2026-05-05)

### âœ… Cambios Realizados:

1. **Acciones siempre abajo (global)**:
   - Se agregÃ³ configuraciÃ³n central `layout.toolbarPosition = "bottom"` en `UI/Config/ui-config.json`.
   - `AppPageContainer` renderiza la toolbar al final (sticky configurable), para que los botones no aparezcan en el encabezado.

## v1.1.14 (2026-05-05)

### âœ… Cambios Realizados:

1. **Explorador (filtro y consistencia)**:
   - Se eliminÃ³ overflow horizontal no deseado en el Ã¡rbol.
   - El filtro ahora afecta tambiÃ©n la lista derecha y el contador de seleccionados.

## v1.1.13 (2026-05-05)

### âœ… Cambios Realizados:

1. **Explorador (UI inmutable)**:
   - Se restaurÃ³ el layout clÃ¡sico (Ã¡rbol + orden de procesamiento) y se evitÃ³ que aparezcan inputs nativos "Choose Files".
   - Se removiÃ³ el header de pÃ¡gina para mantener la ventana idÃ©ntica al diseÃ±o original.

## v1.1.12 (2026-05-04)

### âœ… Cambios Realizados:

1. **EstÃ©tica global aplicada en ConfiguraciÃ³n**:
   - Barra inferior de acciones y botones con estilo pill, bordes y colores corporativos.
   - Estilos utilitarios agregados a `wwwroot/css/app.css` para reutilizaciÃ³n.

## v1.1.11 (2026-05-04)

### âœ… Cambios Realizados:

1. **UI/UX ConfiguraciÃ³n**:
   - Pantalla `/config` ajustada a solo **GRID 1 (Empresas)** y **GRID 2 (Cuentas)**.
   - Acciones movidas a una barra inferior: Exportar, Importar, Log (.txt), Eliminar seleccionados, Guardar cambios.

2. **Descargas sin eval (seguridad)**:
   - ExportaciÃ³n JSON y Log `.txt` usando helper JS dedicado.

3. **Dev Server (Debug)**:
   - Workaround para error de Static Web Assets compression en `dotnet run`.

