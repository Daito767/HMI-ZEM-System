(function () {
  const MODULE_NAME = "front_scale_provider";
  const module = shmi.pkg(MODULE_NAME);

  // Logical drawing size for FRONT view
  const VIEW_WIDTH_MM  = 750;
  const VIEW_HEIGHT_MM = 500;

  // Container to measure (Designer → Control Name)
  const CONTAINER_SELECTOR = ".front-view-container";

  module.run = function (self) {
    console.error("✅ front_scale_provider run() started.");
    const tools = shmi.pkg("custom.tools");
    self.vars = self.vars || {};

    self.vars.cancelable = shmi.onReady({
      controls: { container: CONTAINER_SELECTOR }
    }, function (resolved) {
      const containerEl = resolved.controls.container?.element || null;
      if (!containerEl) {
        console.error(`❌ front_scale_provider: container not found (${CONTAINER_SELECTOR})`);
        return;
      }
      console.error("✅ front_scale_provider: container resolved");

      const publish = () => {
        const scale = tools.computePxPerMm(containerEl, VIEW_WIDTH_MM, VIEW_HEIGHT_MM);
        tools.setPxPerMm(scale); // shared value
      };

      publish(); // initial
      self.vars.onResize = () => publish();
      window.addEventListener("resize", self.vars.onResize);
    });

    self.onDisable = function () {
      console.error("🛑 front_scale_provider disabled");
      if (self.vars.cancelable) self.vars.cancelable.cancel();
      if (self.vars.onResize) window.removeEventListener("resize", self.vars.onResize);
    };
  };
})();
