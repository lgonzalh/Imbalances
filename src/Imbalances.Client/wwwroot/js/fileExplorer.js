window.fileExplorer = {
    getFiles: function (inputElementId, dotNetHelper) {
        const input = document.getElementById(inputElementId);
        if (!input) return;

        input.addEventListener('change', (e) => {
            const files = e.target.files;
            const fileList = [];
            for (let i = 0; i < files.length; i++) {
                const file = files[i];
                if (file.name.endsWith('.xlsx') || file.name.endsWith('.xls')) {
                    fileList.push({
                        name: file.name,
                        path: file.webkitRelativePath || file.name,
                        size: file.size,
                        lastModified: new Date(file.lastModified).toISOString()
                    });
                }
            }
            // Retorna al componente de Blazor
            dotNetHelper.invokeMethodAsync('OnFilesSelectedJS', fileList);
        });
    },
    clickInput: function(inputElementId) {
        document.getElementById(inputElementId).click();
    }
};
