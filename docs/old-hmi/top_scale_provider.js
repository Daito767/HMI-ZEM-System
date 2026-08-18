(function () {
  const MODULE_NAME = "top_scale_provider";
  const module = shmi.pkg(MODULE_NAME);

  // Logical drawing size of your top view (same you used elsewhere)
  const VIEW_WIDTH_MM  = 750;
  const VIEW_HEIGHT_MM = 900;

  // Control Name of the container to measure (update if needed)
  const CONTAINER_SELECTOR = ".top-view-container";

  module.run = function (self) {
    console.error("✅ top_scale_provider run() started.");
    const tools = shmi.pkg("custom.tools");
    self.vars = self.vars || {};

    // Wait for the container to exist
    self.vars.cancelable = shmi.onReady({
      controls: { container: CONTAINER_SELECTOR }
    }, function (resolved) {
      const containerEl = resolved.controls.container?.element || null;
      if (!containerEl) {
        console.error(`❌ top_scale_provider: container not found (${CONTAINER_SELECTOR})`);
        return;
      }
      console.error("✅ top_scale_provider: container resolved");

      const publish = () => {
        const scale = tools.computePxPerMm(containerEl, VIEW_WIDTH_MM, VIEW_HEIGHT_MM);
        tools.setPxPerMm(scale);
      };

      publish(); // set initial
      self.vars.onResize = () => publish();
      window.addEventListener("resize", self.vars.onResize);
    });

    self.onDisable = function () {
      console.error("🛑 top_scale_provider disabled");
      if (self.vars.cancelable) self.vars.cancelable.cancel();
      if (self.vars.onResize) window.removeEventListener("resize", self.vars.onResize);
    };
  };
})();
