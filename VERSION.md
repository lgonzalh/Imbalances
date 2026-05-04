# Imbalances - Registro de Versiones

## v1.1.10 (2026-05-04)

### ✅ Cambios Realizados:

1. **Actualización de Versión**:
   - Actualizado número de versión de v1.1.9 a v1.1.10 en `MainLayout.razor` y archivos `.csproj`.

## v1.1.9 (2026-05-03)

### ✅ Cambios Realizados:

1. **Actualización de Versión**:
   - Actualizado número de versión de v1.1.8 a v1.1.9 en `MainLayout.razor` y archivos `.csproj`.

## v1.1.8 (2026-05-03)

### ✅ Cambios Realizados:

1. **Actualización de Versión**: 
   - Actualizado número de versión de v1.1.7 a v1.1.8 en MainLayout.razor

2. **Mejora en el Módulo Explorador**:
   - ✅ **Botón Iniciar**: Funciona correctamente - procesa archivos Excel seleccionados
   - ✅ **Logs de Procesamiento**: Muestra paso a paso el progreso con timestamps
   - ✅ **Exportación de Logs**: Función completa para exportar logs a archivo de texto
   - ✅ **Barra de Progreso Visual**: **NUEVO** - Agregada barra de progreso linear mostrando:
     - Porcentaje de avance (archivo X de Y)
     - Etapa actual del procesamiento
     - Animación con efecto striped

### 📊 Funcionalidades Verificadas:

- **Explorador de Archivos**: ✅ Carga carpetas/archivos Excel (.xlsx, .xls)
- **Árbol de Navegación**: ✅ Estructura jerárquica con carpetas expandibles
- **Filtro de Búsqueda**: ✅ Búsqueda en tiempo real sobre archivos
- **Selección Múltiple**: ✅ Checkboxes para seleccionar archivos a procesar
- **Procesamiento**: ✅ Extracción de datos de archivos Excel
- **Logs Detallados**: ✅ Mensajes con timestamp, nivel (Info/Success/Error/Warning)
- **Barra de Progreso**: ✅ **NUEVA** - Visual del progreso del procesamiento

### 🔧 Estado del Sistema:

- **Compilación**: ✅ Exitosa (con 9 advertencias menores de null reference)
- **Módulo Explorador**: ✅ Totalmente funcional
- **Botón Iniciar**: ✅ Activo y procesando archivos correctamente
- **Logs Paso a Paso**: ✅ Mostrando progreso detallado archivo por archivo
- **Barra de Progreso**: ✅ **NUEVA** - Visualización en tiempo real del progreso

### 📝 Notas:

El sistema está completamente funcional. La barra de progreso ahora muestra visualmente el avance del procesamiento, complementando los logs de texto existentes.
