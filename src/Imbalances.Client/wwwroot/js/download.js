window.imbalancesDownload = {
  downloadTextFile: (fileName, content, mimeType) => {
    const type = mimeType || "text/plain";
    const blob = new Blob([content ?? ""], { type });
    const url = URL.createObjectURL(blob);

    const a = document.createElement("a");
    a.href = url;
    a.download = fileName || "download.txt";
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    a.remove();

    URL.revokeObjectURL(url);
  },
};

