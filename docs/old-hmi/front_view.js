(function () {
  console.error("⚠️ front_view LOADED!");

  const MODULE_NAME = "front_view";
  const module = shmi.pkg(MODULE_NAME);

  // OPC items
  const ITEM_POSITION         = "ArmPosition";
  const ITEM_VAC_EXT          = "Vacuum_Extended";
  const ITEM_VAC_RET          = "Vacuum_Retracted";
  const ITEM_GRIPPER_CLOSED   = "Gripper_Closed";
  const ITEM_GRIP_EXT         = "Gripper_Extended";
  const ITEM_GRIP_RET         = "Gripper_Retracted";

  // Logical canvas (must match front_scale_provider)
  const SVG_WIDTH_MM  = 750;
  const SVG_HEIGHT_MM = 500;

  // Motion constants
  const SMOOTHING_SPEED = 0.15;
  const MIN_DELTA_PX    = 0.1;
  const DROP_MM         = 40;
  const CLAW_OFFSET_MM  = 3;

  // Designer Control Names
  const SELECTORS = {
    container: ".front-view-container",     // used only if you ever compute scale locally
    pistons:   ".vertical-pistons",
    vacuum:    ".vacuum",
    gripper:   ".gripper",
    clawL:     ".gripper-claw-left",
    clawR:     ".gripper-claw-right"
  };

  const isTrue = v => (v === true || v === 1 || v === "1");

  module.run = function (self) {
    console.error("✅ front_view run() started.");
    const im    = shmi.requires("visuals.session.ItemManager");
    const tools = shmi.pkg("custom.tools");  // shared converter + scale updates
    self.vars = self.vars || {};

    // DOM refs
    const el = { pistons:null, vacuum:null, gripper:null, clawL:null, clawR:null };

    // Targets in mm / currents in px
    let targetXmm = 0;       let currentXpx = 0;
    let vacTargetYmm = 0;    let vacCurrentYpx = 0;
    let gripTargetYmm = 0;   let gripCurrentYpx = 0;
    let clawOffsetPx = 0;    // computed from CLAW_OFFSET_MM on scale change

    // Bit levels (no edge dependency)
    const bits = { vac: { ext:false, ret:false }, grip: { ext:false, ret:false }, closed:false };

    let rafId = null;
    let frame = 0;

    function applyTransform(target, xPx, yPx) {
      if (!target) return;
      target.style.transform = `translate(${xPx}px, ${yPx}px)`;
    }

    function animateStep() {
      const xTargetPx   = tools.mmToPx(targetXmm);
      const vacTargetPx = tools.mmToPx(vacTargetYmm);
      const gripTargetPx= tools.mmToPx(gripTargetYmm);

      const dx  = xTargetPx   - currentXpx;
      const dv  = vacTargetPx - vacCurrentYpx;
      const dg  = gripTargetPx- gripCurrentYpx;

      const chX = Math.abs(dx) > MIN_DELTA_PX;
      const chV = Math.abs(dv) > MIN_DELTA_PX;
      const chG = Math.abs(dg) > MIN_DELTA_PX;

      if (chX) currentXpx   += dx * SMOOTHING_SPEED;
      if (chV) vacCurrentYpx+= dv * SMOOTHING_SPEED;
      if (chG) gripCurrentYpx+= dg * SMOOTHING_SPEED;

      // Apply: all parts move with X; per-part Y
      applyTransform(el.pistons, currentXpx, 0);
      applyTransform(el.vacuum,  currentXpx, vacCurrentYpx);

      // Gripper + claws share gripper Y. Claws have ±clawOffsetPx on X.
      applyTransform(el.gripper, currentXpx, gripCurrentYpx);
      applyTransform(el.clawL,   currentXpx + (bits.closed ?  clawOffsetPx : 0), gripCurrentYpx);
      applyTransform(el.clawR,   currentXpx - (bits.closed ?  clawOffsetPx : 0), gripCurrentYpx);

      if ((frame++ % 10) === 0) {
        console.error(
          `🎞️ front frame x=${currentXpx.toFixed(1)} vY=${vacCurrentYpx.toFixed(1)} gY=${gripCurrentYpx.toFixed(1)} ` +
          `closed=${bits.closed} clawPx=${clawOffsetPx.toFixed(2)}`
        );
      }

      if (chX || chV || chG) {
        rafId = requestAnimationFrame(animateStep);
      } else {
        rafId = null;
      }
    }
    function triggerAnim(){ if (!rafId) animateStep(); }

    // Recompute Y targets from current bit levels (level-based, robust)
    function recomputeTargets() {
      function decide(curr, ext, ret, drop) {
        if (ret && !ext) return 0;
        if (ext && !ret) return drop;
        if (!ext && !ret) return curr; // hold when both false
        return 0; // both true -> fail-safe: retract
      }
      const newVacY  = decide(vacTargetYmm,  bits.vac.ext,  bits.vac.ret,  DROP_MM);
      const newGripY = decide(gripTargetYmm, bits.grip.ext, bits.grip.ret, DROP_MM);

      let changed = false;
      if (newVacY  !== vacTargetYmm)  { vacTargetYmm  = newVacY;  console.error(`🎯 vacuum Y=${vacTargetYmm}mm`);   changed = true; }
      if (newGripY !== gripTargetYmm) { gripTargetYmm = newGripY; console.error(`🎯 gripper Y=${gripTargetYmm}mm`); changed = true; }

      if (changed) triggerAnim();
    }

    // React to shared scale changes (e.g., window resize)
    const unScale = tools.onScale((s)=>{
      clawOffsetPx = tools.mmToPx(CLAW_OFFSET_MM); // keep claws correct on every scale update
      console.error(`🔄 front_view scale update px/mm=${s.toFixed(6)}, clawOffsetPx=${clawOffsetPx.toFixed(2)}`);
      triggerAnim();
    });

    // Resolve widgets
    console.error("🔎 front_view: waiting for widgets via shmi.onReady…");
    self.vars.cancelable = shmi.onReady({
      controls: {
        pistons: SELECTORS.pistons,
        vacuum:  SELECTORS.vacuum,
        gripper: SELECTORS.gripper,
        clawL:   SELECTORS.clawL,
        clawR:   SELECTORS.clawR
      }
    }, function(resolved) {
      el.pistons = resolved.controls.pistons?.element || null;
      el.vacuum  = resolved.controls.vacuum?.element  || null;
      el.gripper = resolved.controls.gripper?.element || null;
      el.clawL   = resolved.controls.clawL?.element   || null;
      el.clawR   = resolved.controls.clawR?.element   || null;

      Object.entries(el).forEach(([k,v])=>{
        console.error(v ? `✅ Widget resolved: ${k}` : `❌ Widget missing: ${k}`);
      });

      // Subscriptions
      const sub = (item, fn) => im.subscribe([item], (n,v)=>{ console.error(`📥 ${n}=${v}`); fn(v); });

      // Arm X
      self.vars.tPos = sub(ITEM_POSITION, v=>{
        if (typeof v === "number") {
          targetXmm = v;
          triggerAnim();
        }
      });

      // Vacuum (level-based)
      self.vars.tVacExt = sub(ITEM_VAC_EXT, v=>{ bits.vac.ext = isTrue(v); recomputeTargets(); });
      self.vars.tVacRet = sub(ITEM_VAC_RET, v=>{ bits.vac.ret = isTrue(v); recomputeTargets(); });

      // Gripper vertical (level-based)
      self.vars.tGripExt = sub(ITEM_GRIP_EXT, v=>{ bits.grip.ext = isTrue(v); recomputeTargets(); });
      self.vars.tGripRet = sub(ITEM_GRIP_RET, v=>{ bits.grip.ret = isTrue(v); recomputeTargets(); });

      // Claw close (instant offset, no smoothing needed beyond overall X/Y)
      self.vars.tGripClosed = sub(ITEM_GRIPPER_CLOSED, v=>{
        bits.closed = isTrue(v);
        // No need to convert here; clawOffsetPx is driven by scale callback
        triggerAnim();
      });
    });

    self.onDisable = function () {
      self.run = false;
      console.error("🛑 front_view disabled");
      // Best-effort unsubscribe across possible token APIs
      for (const key in self.vars) {
        const t = self.vars[key];
        if (!t) continue;
        if (typeof t.unsubscribe === "function") t.unsubscribe();
        else if (typeof t.unlisten === "function") t.unlisten();
      }
      if (self.vars.cancelable) self.vars.cancelable.cancel();
      if (unScale) unScale();
      if (rafId) cancelAnimationFrame(rafId);
    };
  };
})();
