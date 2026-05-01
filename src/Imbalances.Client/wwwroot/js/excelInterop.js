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
    }
};
