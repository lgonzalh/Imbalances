# Imbalances - Registro de Versiones

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
