# Módulo de Administración de Firebase

Este módulo proporciona funciones administrativas para gestionar las colecciones y documentos de Firestore.

## ⚠️ ADVERTENCIA DE SEGURIDAD

**IMPORTANTE**: Estas funciones permiten eliminar datos de forma permanente. En un entorno de producción, deberías:

1. **Implementar autenticación y autorización** - Verificar que solo administradores autorizados puedan acceder
2. **Agregar reglas de seguridad** - Limitar el acceso a estas funciones
3. **Implementar logging** - Registrar todas las operaciones de eliminación
4. **Agregar rate limiting** - Prevenir abuso

### Ejemplo de implementación de seguridad:

```typescript
// Verificar que el usuario es administrador
const auth = req.headers.authorization;
if (!auth || !await verificarAdmin(auth)) {
  res.status(403).json({error: "No autorizado"});
  return;
}
```

## Funciones Disponibles

### 1. listarColecciones
Lista todas las colecciones disponibles en Firestore.

**Endpoint**: `GET /listarColecciones`

**Respuesta**:
```json
{
  "colecciones": ["2026-05", "resultados", "configuracion"]
}
```

### 2. listarDocumentos
Lista los documentos de una colección específica.

**Endpoint**: `GET /listarDocumentos?coleccion=NOMBRE&limite=100`

**Parámetros**:
- `coleccion` (requerido): Nombre de la colección
- `limite` (opcional): Número máximo de documentos a retornar (default: 100)

**Respuesta**:
```json
{
  "documentos": [
    {
      "id": "doc123",
      "data": { ... },
      "preview": "empresa: ACME, periodo: 2026-05, ..."
    }
  ]
}
```

### 3. eliminarDocumentos
Elimina documentos específicos de una colección.

**Endpoint**: `POST /eliminarDocumentos`

**Body**:
```json
{
  "coleccion": "2026-05",
  "documentIds": ["doc1", "doc2", "doc3"]
}
```

**Respuesta**:
```json
{
  "eliminados": 3,
  "errores": 0
}
```

### 4. limpiarColeccion
Elimina TODOS los documentos de una colección.

**Endpoint**: `POST /limpiarColeccion`

**Body**:
```json
{
  "coleccion": "2026-05"
}
```

**Respuesta**:
```json
{
  "eliminados": 150,
  "errores": 0
}
```

## Uso desde la Aplicación

La página de administración está disponible en `/admin` (no visible en el menú de navegación).

### Características:
- ✅ Listar todas las colecciones
- ✅ Ver documentos de cada colección
- ✅ Vista previa de datos en formato JSON
- ✅ Selección múltiple de documentos
- ✅ Eliminar documentos individuales
- ✅ Eliminar documentos seleccionados
- ✅ Limpiar colección completa
- ✅ Confirmaciones dobles para operaciones peligrosas

## Despliegue

Para desplegar las funciones:

```bash
cd functions
npm run build
firebase deploy --only functions
```

## Notas de Implementación

- Las operaciones de eliminación se realizan en lotes (batch) para mejor rendimiento
- El límite de batch de Firestore es 500 operaciones
- Las operaciones son irreversibles - no hay papelera de reciclaje
- Se recomienda hacer backups antes de operaciones masivas
