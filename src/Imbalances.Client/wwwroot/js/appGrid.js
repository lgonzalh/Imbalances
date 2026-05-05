window.appGrid = {
  init: (gridId, minColWidth) => {
    const root = document.querySelector(`[data-grid-id="${gridId}"]`);
    if (!root) return;
    root.style.setProperty("--app-grid-mincol", `${minColWidth || 120}px`);

    const table = root.querySelector("[data-grid-table]");
    const colgroup = table?.querySelector("colgroup");
    if (!colgroup) return;

    const cols = Array.from(colgroup.querySelectorAll("col[data-col-index]"));
    const handles = Array.from(root.querySelectorAll("[data-resize-handle]"));

    const getCol = (idx) => cols.find((c) => c.getAttribute("data-col-index") === String(idx));

    handles.forEach((handle) => {
      handle.addEventListener("mousedown", (ev) => {
        ev.preventDefault();
        ev.stopPropagation();

        const idx = handle.getAttribute("data-col-index");
        if (idx == null) return;

        const col = getCol(idx);
        const th = root.querySelector(`th[data-col-index="${idx}"]`);
        if (!col || !th) return;

        const startX = ev.clientX;
        const startW = th.getBoundingClientRect().width;

        const move = (e) => {
          const delta = e.clientX - startX;
          const nextW = Math.max(minColWidth || 120, startW + delta);
          col.style.width = `${nextW}px`;
        };

        const up = () => {
          document.removeEventListener("mousemove", move);
          document.removeEventListener("mouseup", up);
        };

        document.addEventListener("mousemove", move);
        document.addEventListener("mouseup", up);
      });
    });
  },
};

