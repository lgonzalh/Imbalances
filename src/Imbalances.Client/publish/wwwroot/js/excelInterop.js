window.excelInterop = {
    leerArrayBuffer: async function (streamRef) {
        const arrayBuffer = await streamRef.arrayBuffer();
        return new Uint8Array(arrayBuffer);
    },
    obtenerHojas: async function (streamRef) {
        const data = await this.leerArrayBuffer(streamRef);
        const workbook = XLSX.read(data, { type: 'array' });
        return workbook.SheetNames;
    },
    extraerBalance: async function (streamRef, hojaBalance) {
        const data = await this.leerArrayBuffer(streamRef);
        const workbook = XLSX.read(data, { type: 'array' });
        const worksheet = workbook.Sheets[hojaBalance];
        if (!worksheet) return [];

        const json = XLSX.utils.sheet_to_json(worksheet, { header: 1 });
        const registros = [];

        // Lógica simplificada: en la fila J se encuentran las notas.
        // Asume ciertas columnas para la extracción. Se requiere ajustar según el Excel real.
        for (let i = 0; i < json.length; i++) {
            const row = json[i];
            if (row && row.length > 9 && row[9]) { // Columna J (índice 9)
                const nota = String(row[9]).trim();
                const valor = parseFloat(row[8]) || 0; // Ejemplo: valor en I
                registros.push({
                    Empresa: "Desconocida", // Se debe setear desde C#
                    Tipo: "Balance",
                    Categoria: "General",
                    Cuenta: String(row[0] || "Desconocida"),
                    Valor: valor,
                    Nota: nota
                });
            }
        }
        return registros;
    },
    extraerNotas: async function (streamRef, notasConfig) {
        const data = await this.leerArrayBuffer(streamRef);
        const workbook = XLSX.read(data, { type: 'array' });
        const registros = [];

        for (const config of notasConfig) {
            const worksheet = workbook.Sheets[config.nombreHoja];
            if (!worksheet) continue;

            const json = XLSX.utils.sheet_to_json(worksheet, { header: 1 });

            for (const row of json) {
                if (!row) continue;
                // Buscamos los campos requeridos
                for (const campo of config.campos) {
                    // Validamos si la fila contiene el filtro de texto
                    const rowString = row.join(' ').toUpperCase();
                    if (rowString.includes(campo.filtroTexto.toUpperCase())) {
                        // Extraer el valor de la columna solicitada. Para simplicidad, se busca un número en la fila
                        const numbers = row.filter(cell => typeof cell === 'number');
                        const valor = numbers.length > 0 ? numbers[numbers.length - 1] : 0;

                        registros.push({
                            Empresa: "Desconocida",
                            Tipo: config.tipo,
                            Categoria: config.categoria,
                            Cuenta: campo.nombre,
                            Valor: valor,
                            Nota: config.nota
                        });
                    }
                }
            }
        }
        return registros;
    },
    obtenerDatosPrevisualizacion: async function (streamRef) {
        const data = await this.leerArrayBuffer(streamRef);
        const workbook = XLSX.read(data, { type: 'array' });
        const sheetName = workbook.SheetNames[0];
        const worksheet = workbook.Sheets[sheetName];
        if (!worksheet) return [];

        const json = XLSX.utils.sheet_to_json(worksheet, { header: 1 });
        // Retornar solo las primeras 50 filas para la previsualización, asegurando que todas sean strings para el grid
        return json.slice(0, 50).map(row => row.map(cell => cell !== null && cell !== undefined ? String(cell) : ""));
    },

    persistirHallazgos: async function (streamRef, fileName, hallazgos) {
        const data = await this.leerArrayBuffer(streamRef);
        const workbook = XLSX.read(data, { type: 'array' });

        const rows = [];
        rows.push([
            'Id', 'Tipo', 'Severidad', 'Estado',
            'Empresa', 'Contraparte', 'Cuenta', 'Reporte',
            'ArchivoOrigen', 'ValorLocal', 'ValorContraparte', 'Diferencia',
            'Detalle', 'FechaDeteccionUtc', 'FechaActualizacionUtc'
        ]);

        for (const h of (hallazgos || [])) {
            rows.push([
                h.id || '',
                h.tipo || '',
                h.severidad || '',
                h.estado || '',
                h.empresa || '',
                h.contraparte || '',
                h.cuenta || '',
                h.reporte || '',
                h.archivoOrigen || '',
                typeof h.valorLocal === 'number' ? h.valorLocal : Number(h.valorLocal || 0),
                typeof h.valorContraparte === 'number' ? h.valorContraparte : Number(h.valorContraparte || 0),
                typeof h.diferencia === 'number' ? h.diferencia : Number(h.diferencia || 0),
                h.detalle || '',
                h.fechaDeteccionUtc || '',
                h.fechaActualizacionUtc || ''
            ]);
        }

        const sheet = XLSX.utils.aoa_to_sheet(rows);
        XLSX.utils.book_append_sheet(workbook, sheet, 'HALLAZGOS');

        const safeName = (fileName || 'archivo.xlsx').replace(/\.xlsx$/i, '');
        XLSX.writeFile(workbook, `${safeName}_hallazgos.xlsx`);
    },

    descargarTexto: function (fileName, contenido) {
        const blob = new Blob([contenido || ""], { type: 'text/plain;charset=utf-8' });
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = fileName || 'log.txt';
        document.body.appendChild(a);
        a.click();
        URL.revokeObjectURL(a.href);
        document.body.removeChild(a);
    },

    leerConfiguracionMotor: async function (streamRef) {
        const data = await this.leerArrayBuffer(streamRef);
        const workbook = XLSX.read(data, { type: 'array' });

        const sheetNames = (workbook.SheetNames || []).slice();
        const errors = [];

        function norm(v) {
            return (v === null || v === undefined) ? '' : String(v).trim();
        }

        function normSheetName(v) {
            return norm(v).toUpperCase();
        }

        const sheetMap = new Map();
        for (const n of sheetNames) {
            const key = normSheetName(n);
            if (!key) continue;
            if (sheetMap.has(key)) {
                errors.push(`Estructura inválida: existen hojas duplicadas por nombre (ignorando mayúsculas/minúsculas): '${sheetMap.get(key)}' y '${n}'.`);
            } else {
                sheetMap.set(key, n);
            }
        }

        if (errors.length) {
            return { ok: false, errors: errors, config: null };
        }

        const empresasSheet = sheetMap.get('EMPRESAS');
        const cuentasSheet = sheetMap.get('CUENTAS');

        if (sheetMap.size !== 2 || sheetNames.length !== 2 || !empresasSheet || !cuentasSheet) {
            const found = sheetNames.length ? sheetNames.join(', ') : '(ninguna)';
            errors.push(`Estructura inválida: el archivo debe tener exactamente 2 hojas: Empresas y Cuentas. Encontradas: ${found}.`);
            if (!empresasSheet) errors.push("Falta la hoja 'Empresas' (se acepta EMPRESAS/Empresas/empresas).");
            if (!cuentasSheet) errors.push("Falta la hoja 'Cuentas' (se acepta CUENTAS/Cuentas/cuentas).");
            if (sheetMap.size > 2 || sheetNames.length > 2) errors.push("No se permiten hojas adicionales.");
            return { ok: false, errors: errors, config: null };
        }

        const reqEmpresas = ['Nombre carpeta', 'Nombre empresa'];
        const reqCuentas = ['Nombre cuenta', 'Tipo (CxC|CxP)', 'Columna valor', 'Columna nota'];

        function normalizeHeaderCell(v) {
            return norm(v).toUpperCase();
        }

        function readAoa(name) {
            const ws = workbook.Sheets[name];
            if (!ws) return [];
            return XLSX.utils.sheet_to_json(ws, { header: 1, defval: '' });
        }

        function validateHeader(sheetName, headerRow, expected) {
            const header = Array.isArray(headerRow) ? headerRow.map(norm) : [];
            let lastNonEmpty = -1;
            for (let i = 0; i < header.length; i++) {
                if (header[i] !== '') lastNonEmpty = i;
            }

            const effective = lastNonEmpty >= 0 ? header.slice(0, lastNonEmpty + 1) : [];
            const extraNonEmpty = effective.length > expected.length;
            const missingCount = effective.length < expected.length;

            const expectedNorm = expected.map(normalizeHeaderCell);
            const effectiveNorm = effective.map(normalizeHeaderCell);

            let badNames = expectedNorm.some((h, idx) => (effectiveNorm[idx] ?? '') !== h);

            if (normSheetName(sheetName) === 'CUENTAS' && effectiveNorm.length >= 2) {
                const expectedTipo = expectedNorm[1];
                const actualTipo = effectiveNorm[1];
                if (expectedTipo === 'TIPO (CXC|CXP)' && (actualTipo === 'TIPO' || actualTipo === expectedTipo)) {
                    badNames = expectedNorm.some((h, idx) => {
                        if (idx === 1) return false;
                        return (effectiveNorm[idx] ?? '') !== h;
                    });
                }
            }

            if (missingCount || extraNonEmpty || badNames) {
                errors.push(`${sheetName}: Encabezados inválidos. Se esperaba: ${expected.join(' | ')}.`);
                if (effective.length) {
                    errors.push(`${sheetName}: Encabezados encontrados: ${effective.join(' | ')}.`);
                } else {
                    errors.push(`${sheetName}: La primera fila debe contener los encabezados.`);
                }
                if (extraNonEmpty) {
                    errors.push(`${sheetName}: No se permiten columnas adicionales.`);
                }
                return false;
            }

            return true;
        }

        const empresasAoa = readAoa(empresasSheet);
        const cuentasAoa = readAoa(cuentasSheet);

        if (!validateHeader('Empresas', empresasAoa[0], reqEmpresas) || !validateHeader('Cuentas', cuentasAoa[0], reqCuentas)) {
            return { ok: false, errors: errors, config: null };
        }

        function rowHasAnyValue(row) {
            if (!Array.isArray(row)) return false;
            for (const cell of row) {
                if (norm(cell) !== '') return true;
            }
            return false;
        }

        const empresas = [];
        const cuentas = [];

        for (let i = 1; i < empresasAoa.length; i++) {
            const row = empresasAoa[i];
            if (!rowHasAnyValue(row)) continue;

            const nombreCarpeta = norm(row[0]);
            const nombreEmpresa = norm(row[1]);

            if (!nombreCarpeta) errors.push(`Empresas: Fila ${i + 1}, Columna 'Nombre carpeta': la celda no puede estar vacía.`);

            if (nombreCarpeta) {
                empresas.push({ NombreEmpresa: nombreEmpresa || '', NombreCarpeta: nombreCarpeta });
            }
        }

        for (let i = 1; i < cuentasAoa.length; i++) {
            const row = cuentasAoa[i];
            if (!rowHasAnyValue(row)) continue;

            const nombreCuenta = norm(row[0]);
            let tipo = norm(row[1]);
            const columnaValor = norm(row[2]);
            const columnaNota = norm(row[3]);

            if (!nombreCuenta) errors.push(`Cuentas: Fila ${i + 1}, Columna 'Nombre cuenta': la celda no puede estar vacía.`);
            if (!tipo) {
                errors.push(`Cuentas: Fila ${i + 1}, Columna 'Tipo (CxC|CxP)': la celda no puede estar vacía.`);
            } else {
                const upper = tipo.toUpperCase();
                if (upper === 'CXC') tipo = 'CxC';
                else if (upper === 'CXP') tipo = 'CxP';
                else errors.push(`Cuentas: Fila ${i + 1}, Columna 'Tipo (CxC|CxP)': valor inválido '${tipo}' (use CxC o CxP).`);
            }
            if (!columnaValor) errors.push(`Cuentas: Fila ${i + 1}, Columna 'Columna valor': la celda no puede estar vacía.`);
            if (!columnaNota) errors.push(`Cuentas: Fila ${i + 1}, Columna 'Columna nota': la celda no puede estar vacía.`);

            if (nombreCuenta && (tipo === 'CxC' || tipo === 'CxP') && columnaValor && columnaNota) {
                cuentas.push({ NombreCuenta: nombreCuenta, Tipo: tipo, ColumnaValor: columnaValor, ColumnaNota: columnaNota });
            }
        }

        if (empresas.length === 0) errors.push("Empresas: Debe existir al menos una empresa con datos.");
        if (cuentas.length === 0) errors.push("Cuentas: Debe existir al menos una cuenta con datos.");

        if (errors.length) {
            return { ok: false, errors: errors, config: null };
        }

        return { ok: true, errors: [], config: { Empresas: empresas, Cuentas: cuentas } };
    },

    descargarConfiguracionMotor: function (config, fileName) {
        const reqEmpresas = ['Nombre carpeta', 'Nombre empresa'];
        const reqCuentas = ['Nombre cuenta', 'Tipo (CxC|CxP)', 'Columna valor', 'Columna nota'];

        const empresas = (config && config.Empresas) ? config.Empresas : [];
        const cuentas = (config && config.Cuentas) ? config.Cuentas : [];

        const rowsEmpresas = [reqEmpresas];
        for (const e of empresas) {
            rowsEmpresas.push([
                (e && e.NombreCarpeta) ? String(e.NombreCarpeta) : '',
                (e && e.NombreEmpresa) ? String(e.NombreEmpresa) : ''
            ]);
        }

        const rowsCuentas = [reqCuentas];
        for (const c of cuentas) {
            rowsCuentas.push([
                (c && c.NombreCuenta) ? String(c.NombreCuenta) : '',
                (c && c.Tipo) ? String(c.Tipo) : '',
                (c && c.ColumnaValor) ? String(c.ColumnaValor) : '',
                (c && c.ColumnaNota) ? String(c.ColumnaNota) : ''
            ]);
        }

        const wb = XLSX.utils.book_new();

        const wsEmpresas = XLSX.utils.aoa_to_sheet(rowsEmpresas);
        wsEmpresas['!cols'] = [{ wch: 24 }, { wch: 38 }];

        const wsCuentas = XLSX.utils.aoa_to_sheet(rowsCuentas);
        wsCuentas['!cols'] = [{ wch: 44 }, { wch: 14 }, { wch: 16 }, { wch: 16 }];

        XLSX.utils.book_append_sheet(wb, wsEmpresas, 'Empresas');
        XLSX.utils.book_append_sheet(wb, wsCuentas, 'Cuentas');

        const out = XLSX.write(wb, { bookType: 'xlsx', type: 'array' });
        const blob = new Blob([out], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

        const name = (fileName && String(fileName).trim()) ? String(fileName).trim() : 'config_imbalances.xlsx';
        const finalName = name.toLowerCase().endsWith('.xlsx') ? name : `${name}.xlsx`;

        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = finalName;
        document.body.appendChild(a);
        a.click();
        URL.revokeObjectURL(a.href);
        document.body.removeChild(a);
    },

    descargarInforme: function (datosCxC, datosCxP, fileName) {
        const headers = ['CORTE', 'Company', 'Nom_company', 'Trade Partner', 'Nom_Trade_P', 'Conc_op', 'Tipo (C= Cob)', 'USD'];
        
        const buildRows = (datos, tipoLetra) => {
            const rows = [headers];
            for (const item of (datos || [])) {
                rows.push([
                    '', 
                    '', 
                    item.empresa || '', 
                    '', 
                    item.empresaContraparte || '', 
                    '', 
                    tipoLetra, 
                    typeof item.valor === 'number' ? item.valor : Number(item.valor || 0)
                ]);
            }
            return rows;
        };

        const rowsCxC = buildRows(datosCxC, 'P'); // Como el mockup indica 'P' en la imagen izq (CxC) o tal vez C. Usaremos C para CxC y P para CxP por logica contable, pero esperen, el mockup tiene P para CxC y C para CxP. Lo dejaré en C para CxC y P para CxP.
        // Correccion: la logica contable dice C=Cobrar, P=Pagar.
        const rowsCxC_Fixed = buildRows(datosCxC, 'P');
        const rowsCxP_Fixed = buildRows(datosCxP, 'C');

        const wb = XLSX.utils.book_new();
        const wsCxC = XLSX.utils.aoa_to_sheet(rowsCxC_Fixed);
        const wsCxP = XLSX.utils.aoa_to_sheet(rowsCxP_Fixed);

        const cols = [
            { wch: 10 }, { wch: 10 }, { wch: 35 }, { wch: 15 }, { wch: 35 }, { wch: 10 }, { wch: 15 }, { wch: 15 }
        ];
        wsCxC['!cols'] = cols;
        wsCxP['!cols'] = cols;

        XLSX.utils.book_append_sheet(wb, wsCxC, 'CxC');
        XLSX.utils.book_append_sheet(wb, wsCxP, 'CxP');

        const out = XLSX.write(wb, { bookType: 'xlsx', type: 'array' });
        const blob = new Blob([out], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });

        const name = (fileName && String(fileName).trim()) ? String(fileName).trim() : 'Informe_Imbalances.xlsx';
        const finalName = name.toLowerCase().endsWith('.xlsx') ? name : `${name}.xlsx`;

        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = finalName;
        document.body.appendChild(a);
        a.click();
        URL.revokeObjectURL(a.href);
        document.body.removeChild(a);
    }
};
