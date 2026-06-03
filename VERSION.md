# Imbalances - Registro de Versiones

## v1.1.24 (2026-06-03)

### 🔧 DEBUGGING Cloud Functions - Paso 4: Fix acceso a propiedades incorrecto en guardarConfiguracion

**BUG CRÍTICO ENCONTRADO**: En `functions/src/configuracion/guardarConfiguracion.ts` línea 72, el código accedía a `n.identificador` pero el modelo NotaConfig tiene `nota` y `nombreHoja`.

**Solución Paso 4**:
- Corregido acceso a `n.nota` en lugar de `n.identificador`
- Agregado fallback a "nota" en caso de undefined

**Archivos Modificados**:
- `functions/src/configuracion/guardarConfiguracion.ts` (línea 72)

**Verificación**: Guardar notas debe funcionar sin errores silenciosos en Firebase.

## v1.1.23 (2026-06-03)

### 🔧 DEBUGGING Deserialization - Paso 3: JsonSerializerContext Global para Blazor JSInterop

**Problema**: Incluso con [JsonPropertyName], el JSInterop de Blazor también necesita opciones de serialización.

**Solución Paso 3**:
- Creado `AppJsonSerializerContext` con `@JsonSourceGenerationOptions` para configurar PropertyNamingPolicy = CamelCase
- Configurado en Program.cs para uso global
- Asegura que TODAS las deserializaciones (Firebase HTTP + localStorage/IndexedDB) usen las mismas opciones

**Archivos**:
- `src/Imbalances.Client/JsonSerializerContext.cs` (nuevo)
- `src/Imbalances.Client/Program.cs` (actualizado)

**Verificación**: Las empresas deben renderizar correctamente tanto al cargar de Firebase como de localStorage.

## v1.1.22 (2026-06-03)

### 🔧 DEBUGGING Deserialization - Paso 2: JsonSerializerOptions en GuardarConfiguracionAsync

**Problema**: Cuando se guardan cambios, el `PostAsJsonAsync` tampoco usa las opciones de serialización.

**Solución Paso 2**: 
- `GuardarConfiguracionAsync` ahora también usa `JsonSerializerOptions` con CamelCase
- Asegura que la serialización de envío sea consistente con lo que Firebase Cloud Function espera

**Verificación**: Guardar cambios y verificar en Firestore que se actualicen correctamente.

## v1.1.21 (2026-06-03)

### 🔧 DEBUGGING Deserialization - Paso 1: JsonSerializerOptions Global

**Problema**: Las empresas no se renderizan tras desproteger porque `ReadFromJsonAsync` no tiene opciones de naming policy.

**Solución Paso 1**: Agregar JsonSerializerOptions con CamelCase naming policy en FirebaseMotorsService

**Cambios**:
- FirebaseMotorsService ahora usa `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` en `ObtenerConfiguracionAsync`
- Esto asegura que el JSON camelCase de Firestore se deserialice correctamente a propiedades PascalCase de C#

**Verificación**: Si las empresas aparecen, entonces este era el problema.

## v1.1.20 (2026-06-03)

### ✅ Cambios Realizados:

1. **JsonPropertyName en modelos**: Agregados atributos `[JsonPropertyName]` explícitos en EmpresaConfig, CuentaConfig, NotaConfig, CuentaEquivalencia y ConfiguracionCore.

## v1.1.18 (2026-05-07)

### ✅ Cambios Realizados:

1. **UX/UI Updates**: Explorador sin campos redundantes, LOG compacto dentro del panel, botones homologados (rect 4px), títulos de página eliminados en Config/Análisis/Informe.
2. **Fix Motor 3**: Eliminada dependencia de `collectionGroup` con índice compuesto; Motor 3 itera jerarquía `empresas/{id}/periodos/{p}/movimientos`. Motor 2 materializa documento padre de empresa.
3. **Fix OnFilesSelectedJS**: Firma corregida para recibir `(string inputId, List<FileJSData> files)` — botón Iniciar habilitado correctamente.
4. **Log en Configuración**: Botón Log exporta el mismo log del Explorador (vía `IProgressService`).
5. **Firestore Rules**: Lectura habilitada para `cruces`, `config_*` colecciones.
6. **Deploy**: Functions desplegadas a `southamerica-east1`.

## v1.1.15 (2026-05-05)

### ✅ Cambios Realizados:

1. **Acciones siempre abajo (global)**:
   - Se agregó configuración central `layout.toolbarPosition = "bottom"` en `UI/Config/ui-config.json`.
   - `AppPageContainer` renderiza la toolbar al final (sticky configurable), para que los botones no aparezcan en el encabezado.

## v1.1.14 (2026-05-05)

### ✅ Cambios Realizados:

1. **Explorador (filtro y consistencia)**:
   - Se eliminó overflow horizontal no deseado en el árbol.
   - El filtro ahora afecta también la lista derecha y el contador de seleccionados.

## v1.1.13 (2026-05-05)

### ✅ Cambios Realizados:

1. **Explorador (UI inmutable)**:
   - Se restauró el layout clásico (árbol + orden de procesamiento) y se evitó que aparezcan inputs nativos "Choose Files".
   - Se removió el header de página para mantener la ventana idéntica al diseño original.

## v1.1.12 (2026-05-04)

### ✅ Cambios Realizados:

1. **Estética global aplicada en Configuración**:
   - Barra inferior de acciones y botones con estilo pill, bordes y colores corporativos.
   - Estilos utilitarios agregados a `wwwroot/css/app.css` para reutilización.

## v1.1.11 (2026-05-04)

### ✅ Cambios Realizados:

1. **UI/UX Configuración**:
   - Pantalla `/config` ajustada a solo **GRID 1 (Empresas)** y **GRID 2 (Cuentas)**.
   - Acciones movidas a una barra inferior: Exportar, Importar, Log (.txt), Eliminar seleccionados, Guardar cambios.

2. **Descargas sin eval (seguridad)**:
   - Exportación JSON y Log `.txt` usando helper JS dedicado.

3. **Dev Server (Debug)**:
   - Workaround para error de Static Web Assets compression en `dotnet run`.
