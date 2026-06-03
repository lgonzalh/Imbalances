# Motor 3 — Cruce Intercompany

## Componentes (código)

- Endpoint HTTP: `cruzar` en `cruceIntercompany.ts`
- Export público del motor: `index.ts`

## Contrato del endpoint

- Método: POST
- Ruta (Cloud Function): `/cruzar`

Body:

```json
{ "periodo": "YYYY-MM", "tolerancia": 0 }
```

## Datos origen (Firestore)

- Entrada: `empresas/*/periodos/{periodo}/movimientos/*`
  - Requiere campos: `empresa_origen`, `empresa_contraparte`, `tipo` (CxC/CxP), `valor`, `periodo`

## Salida (Firestore)

- Resultados: `cruces/{periodo}/resultados/{id}`

Ejemplo:

```json
{
  "empresa_a": "SASA",
  "empresa_b": "ENDPOINTS",
  "cxC": 723,
  "cxP": 723,
  "diferencia": 0,
  "estado": "OK"
}
```

## Reglas

- Normalización obligatoria (nombres y tipo)
- Cruce CxC vs CxP usando clave invertida para CxP
- Omitir resultados si ambos lados son 0
- No duplicar pares A-B / B-A: el `id` es determinístico por par ordenado

