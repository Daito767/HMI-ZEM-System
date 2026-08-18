(function () {
  const MODULE = "custom.tools";
  const module = shmi.pkg(MODULE);

  // ---- internal state ----
  let pxPerMm = 1;
  const listeners = new Set();

  function notify() {
    for (const cb of listeners) {
      try { cb(pxPerMm); } catch (e) { console.error("[custom.tools] onScale listener error:", e); }
    }
  }

  // ---- public API ----
  module.setPxPerMm = function (v) {
    if (typeof v === "number" && isFinite(v) && v > 0 && v !== pxPerMm) {
      pxPerMm = v;
      console.error(`📐 [custom.tools] setPxPerMm=${v.toFixed(6)}`);
      notify();
    }
  };

  module.getPxPerMm = function () { return pxPerMm; };

  module.mmToPx = function (mm) { return mm * pxPerMm; };
  module.pxToMm = function (px) { return px / pxPerMm; };

  /**
   * Subscribe to scale changes. Returns an unsubscribe function.
   */
  module.onScale = function (cb) {
    if (typeof cb === "function") {
      listeners.add(cb);
      // give current immediately (useful on late subscription)
      try { cb(pxPerMm); } catch {}
      return () => listeners.delete(cb);
    }
    return () => {};
  };

  /**
   * Utility to compute px/mm from a container’s rect and a logical mm canvas.
   */
  module.computePxPerMm = function (containerEl, logicalWidthMm, logicalHeightMm) {
    const r = logicalWidthMm / logicalHeightMm;
    const rect = containerEl?.getBoundingClientRect?.();
    if (rect && rect.width > 0 && rect.height > 0) {
      return (rect.width / rect.height <= r)
        ? (rect.width  / logicalWidthMm)
        : (rect.height / logicalHeightMm);
    }
    // fallback to window
    const sr = window.innerWidth / window.innerHeight;
    return (sr <= r)
      ? (window.innerWidth  / logicalWidthMm)
      : (window.innerHeight / logicalHeightMm);
  };

  console.error("✅ custom.tools loaded");
})();
