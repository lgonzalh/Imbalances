# Imbalances

Imbalances es una solución de ingeniería financiera desarrollada en C# que optimiza la auditoría de holdings mediante el procesamiento acelerado de volúmenes diversos de archivos Excel. 

La arquitectura permite una integración recursiva con OneDrive, validando de forma ágil la reciprocidad entre Cuentas por Cobrar (CxC) y Cuentas por Pagar (CxP). 

Al operar directamente sobre los archivos fuente, el sistema garantiza una trazabilidad inmediata sin necesidad de infraestructura de base de datos adicional. 

Procesamiento Masivo y Ágil: Ejecución de algoritmos de lectura rápida sobre múltiples libros de Excel distribuidos en diversas subcarpetas para un análisis simultáneo de datos.

Funcionalidades:

Auditoría de Reciprocidad: Identificación precisa de desequilibrios financieros entre entidades del holding mediante el cruce de cuentas relacionadas.

Validación de Disponibilidad Documental: Monitoreo automatizado de la presencia o ausencia de reportes críticos para el cierre mensual.

Persistencia Directa en Origen: Gestión de hallazgos y estados de verificación realizada íntegramente sobre las plantillas originales, asegurando la portabilidad de la información.

Panel de Disconformidades en Tiempo Real: Visualización técnica de hallazgos para agilizar la toma de decisiones y correcciones contables.
