(function () {
  console.error("⚠️ top_view LOADED!");

  var MODULE_NAME = "top_view",
      ENABLE_LOGGING = false,
      RECORD_LOG = false,
      logger = shmi.requires("visuals.tools.logging").createLogger(MODULE_NAME, ENABLE_LOGGING, RECORD_LOG),
      module = shmi.pkg(MODULE_NAME);

  // Logical canvas for TOP view
  const SVG_WIDTH_MM  = 750;
  const SVG_HEIGHT_MM = 900;

  // Motion constants
  const SMOOTHING_SPEED = 0.15;
  const MIN_DELTA_PX    = 0.1;
  const PULLER_DROP_MM        = 124;
  const PUSHER_PISTON_DROP_MM = 310;
  const PUSHER_DROP_MM        = 90;
  const STORAGE_DELTA_MM      = 10;

  // OPC / alias names (edit here if your aliases differ)
  const ITEM_POS       = "ArmPosition";

  const ITEM_RPULL_EXT = "Right_Puller_Extended";
  const ITEM_RPULL_RET = "Right_Puller_Retracted";
  const ITEM_LPULL_EXT = "Left_Puller_Extended";
  const ITEM_LPULL_RET = "Left_Puller_Retracted";

  const ITEM_RPIS_EXT  = "Right_Pusher_Piston_Extended";
  const ITEM_RPIS_RET  = "Right_Pusher_Piston_Retracted";
  const ITEM_RPUSH_EXT = "Right_Pusher_Extended";
  const ITEM_RPUSH_RET = "Right_Pusher_Retracted";

  const ITEM_LPIS_EXT  = "Left_Pusher_Piston_Extended";
  const ITEM_LPIS_RET  = "Left_Pusher_Piston_Retracted";
  const ITEM_LPUSH_EXT = "Left_Pusher_Extended";
  const ITEM_LPUSH_RET = "Left_Pusher_Retracted";

  const ITEM_ST_TOP_FWD   = "Storage_Top_Forward";
  const ITEM_ST_TOP_BWD   = "Storage_Top_Backward";
  const ITEM_ST_BOT_RIGHT = "Storage_Bottom_Right";
  const ITEM_ST_BOT_LEFT  = "Storage_Bottom_Left";

  // Designer Control Names (must match your data-name / Control Name)
  const SELECTORS = {
    container: ".top-view-container",
    arm: ".arm",
    rightPuller: ".right-puller",
    leftPuller: ".left-puller",
    rightPullerPiston: ".right-puller-piston",
    leftPullerPiston: ".left-puller-piston",
    rightPiston: ".right-pusher-piston",
    rightPusher: ".right-pusher",
    leftPiston: ".left-pusher-piston",
    leftPusher: ".left-pusher",
    upperUp: ".upper-up",
    upperDown: ".upper-down",
    lowerRight: ".lower-right",
    lowerLeft: ".lower-left"
  };

  const isTrue = v => (v === true || v === 1 || v === "1");

  // ---- SAFE tools wrapper (use shared custom.tools if present; fallback otherwise) ----
  const tools = (function () {
    const t = shmi.pkg("custom.tools") || {};
    const has =
      typeof t.getPxPerMm === "function" &&
      typeof t.mmToPx     === "function" &&
      typeof t.onScale    === "function";

    if (has) {
      console.error("🔗 top_view using shared custom.tools");
      return t;
    }

    console.error("⚠️ custom.tools missing — top_view using local scale fallback");

    // local fallback tools
    let pxPerMm = 1;
    const listeners = new Set();

    function computePxPerMm(containerEl) {
      const r = SVG_WIDTH_MM / SVG_HEIGHT_MM;
      const rect = containerEl?.getBoundingClientRect?.();
      if (rect && rect.width > 0 && rect.height > 0) {
        return (rect.width / rect.height <= r) ? (rect.width / SVG_WIDTH_MM)
                                               : (rect.height / SVG_HEIGHT_MM);
      }
      const sr = window.innerWidth / window.innerHeight;
      return (sr <= r) ? (window.innerWidth / SVG_WIDTH_MM)
                       : (window.innerHeight / SVG_HEIGHT_MM);
    }

    const api = {
      getPxPerMm: () => pxPerMm,
      mmToPx    : mm => mm * pxPerMm,
      pxToMm    : px => px / pxPerMm,
      onScale   : (cb) => { listeners.add(cb); try { cb(pxPerMm); } catch {} return () => listeners.delete(cb); },
      _setPxPerMm: (v) => { pxPerMm = v; for (const cb of listeners) try { cb(v); } catch {} }
    };

    // initialize & update on resize
    shmi.onReady({ controls: { container: SELECTORS.container } }, (resolved) => {
      const containerEl = resolved.controls.container?.element || null;
      api._setPxPerMm(computePxPerMm(containerEl));
      window.addEventListener("resize", () => api._setPxPerMm(computePxPerMm(containerEl)));
    });

    return api;
  })();

  // ---- module.run --------------------------------------------------------
  module.run = function (self) {
    console.error("✅ top_view run() started.");
    const im = shmi.requires("visuals.session.ItemManager");
    self.vars = self.vars || {};

    // DOM refs
    const els = {
      container:null, arm:null,
      rightPuller:null, leftPuller:null,
      rightPullerPiston:null, leftPullerPiston:null,
      rightPiston:null, rightPusher:null,
      leftPiston:null,  leftPusher:null,
      upperUp:null, upperDown:null, lowerRight:null, lowerLeft:null
    };

    // Targets in mm; currents in px
    let armTargetXmm = 0, armCurrentXpx = 0;

    let rPullerTargetYmm = 0, rPullerCurrentYpx = 0;
    let lPullerTargetYmm = 0, lPullerCurrentYpx = 0;

    let rPistonTargetYmm = 0, rPistonCurrentYpx = 0;
    let rPusherRelTargetYmm = 0, rPusherRelCurrentYpx = 0;

    let lPistonTargetYmm = 0, lPistonCurrentYpx = 0;
    let lPusherRelTargetYmm = 0, lPusherRelCurrentYpx = 0;

    // Storage movers
    let upperUpTargetYmm = 0,   upperUpCurrentYpx = 0;
    let upperDownTargetYmm = 0, upperDownCurrentYpx = 0;
    let lowerRightTargetXmm = 0, lowerRightCurrentXpx = 0;
    let lowerLeftTargetXmm  = 0, lowerLeftCurrentXpx  = 0;

    // Level states (no edge dependency)
    const bits = {
      rPull: { ext:false, ret:false },
      lPull: { ext:false, ret:false },
      rPis:  { ext:false, ret:false },
      rRel:  { ext:false, ret:false },
      lPis:  { ext:false, ret:false },
      lRel:  { ext:false, ret:false }
    };

    let rafId = null;
    let frame = 0;

    function applyTransform(node, xPx, yPx) {
      if (node) node.style.transform = `translate(${xPx}px, ${yPx}px)`;
    }

    function animateStep() {
      const dx      = tools.mmToPx(armTargetXmm)        - armCurrentXpx;
      const dyRPull = tools.mmToPx(rPullerTargetYmm)    - rPullerCurrentYpx;
      const dyLPull = tools.mmToPx(lPullerTargetYmm)    - lPullerCurrentYpx;
      const dyRPis  = tools.mmToPx(rPistonTargetYmm)    - rPistonCurrentYpx;
      const dyRRel  = tools.mmToPx(rPusherRelTargetYmm) - rPusherRelCurrentYpx;
      const dyLPis  = tools.mmToPx(lPistonTargetYmm)    - lPistonCurrentYpx;
      const dyLRel  = tools.mmToPx(lPusherRelTargetYmm) - lPusherRelCurrentYpx;

      const dyUpperUp    = tools.mmToPx(upperUpTargetYmm)   - upperUpCurrentYpx;
      const dyUpperDown  = tools.mmToPx(upperDownTargetYmm) - upperDownCurrentYpx;
      const dxLowerRight = tools.mmToPx(lowerRightTargetXmm)- lowerRightCurrentXpx;
      const dxLowerLeft  = tools.mmToPx(lowerLeftTargetXmm) - lowerLeftCurrentXpx;

      const changed =
        Math.abs(dx) > MIN_DELTA_PX ||
        Math.abs(dyRPull) > MIN_DELTA_PX ||
        Math.abs(dyLPull) > MIN_DELTA_PX ||
        Math.abs(dyRPis)  > MIN_DELTA_PX ||
        Math.abs(dyRRel)  > MIN_DELTA_PX ||
        Math.abs(dyLPis)  > MIN_DELTA_PX ||
        Math.abs(dyLRel)  > MIN_DELTA_PX ||
        Math.abs(dyUpperUp)   > MIN_DELTA_PX ||
        Math.abs(dyUpperDown) > MIN_DELTA_PX ||
        Math.abs(dxLowerRight)> MIN_DELTA_PX ||
        Math.abs(dxLowerLeft) > MIN_DELTA_PX;

      if (Math.abs(dx)      > MIN_DELTA_PX) armCurrentXpx        += dx      * SMOOTHING_SPEED;
      if (Math.abs(dyRPull) > MIN_DELTA_PX) rPullerCurrentYpx    += dyRPull * SMOOTHING_SPEED;
      if (Math.abs(dyLPull) > MIN_DELTA_PX) lPullerCurrentYpx    += dyLPull * SMOOTHING_SPEED;
      if (Math.abs(dyRPis)  > MIN_DELTA_PX) rPistonCurrentYpx    += dyRPis  * SMOOTHING_SPEED;
      if (Math.abs(dyRRel)  > MIN_DELTA_PX) rPusherRelCurrentYpx += dyRRel  * SMOOTHING_SPEED;
      if (Math.abs(dyLPis)  > MIN_DELTA_PX) lPistonCurrentYpx    += dyLPis  * SMOOTHING_SPEED;
      if (Math.abs(dyLRel)  > MIN_DELTA_PX) lPusherRelCurrentYpx += dyLRel  * SMOOTHING_SPEED;

      if (Math.abs(dyUpperUp)   > MIN_DELTA_PX) upperUpCurrentYpx   += dyUpperUp   * SMOOTHING_SPEED;
      if (Math.abs(dyUpperDown) > MIN_DELTA_PX) upperDownCurrentYpx += dyUpperDown * SMOOTHING_SPEED;
      if (Math.abs(dxLowerRight)> MIN_DELTA_PX) lowerRightCurrentXpx+= dxLowerRight* SMOOTHING_SPEED;
      if (Math.abs(dxLowerLeft) > MIN_DELTA_PX) lowerLeftCurrentXpx += dxLowerLeft * SMOOTHING_SPEED;

      // Apply transforms
      applyTransform(els.arm, armCurrentXpx, 0);

      applyTransform(els.rightPuller, 0, rPullerCurrentYpx);
      applyTransform(els.leftPuller,  0, lPullerCurrentYpx);

      applyTransform(els.rightPullerPiston, 0, 0);
      applyTransform(els.leftPullerPiston,  0, 0);

      applyTransform(els.rightPiston, 0, rPistonCurrentYpx);
      applyTransform(els.leftPiston,  0, lPistonCurrentYpx);

      applyTransform(els.rightPusher, 0, rPistonCurrentYpx + rPusherRelCurrentYpx);
      applyTransform(els.leftPusher,  0, lPistonCurrentYpx + lPusherRelCurrentYpx);

      applyTransform(els.upperUp,    0, upperUpCurrentYpx);
      applyTransform(els.upperDown,  0, upperDownCurrentYpx);
      applyTransform(els.lowerRight, lowerRightCurrentXpx, 0);
      applyTransform(els.lowerLeft,  lowerLeftCurrentXpx,  0);

      if ((frame++ % 10) === 0) {
        console.error(
          `🎞️ top frame armX=${armCurrentXpx.toFixed(1)} ` +
          `RPull=${rPullerCurrentYpx.toFixed(1)} LPull=${lPullerCurrentYpx.toFixed(1)} ` +
          `RPis=${rPistonCurrentYpx.toFixed(1)} RRel=${rPusherRelCurrentYpx.toFixed(1)} ` +
          `LPis=${lPistonCurrentYpx.toFixed(1)} LRel=${lPusherRelCurrentYpx.toFixed(1)} | ` +
          `U↑=${upperUpCurrentYpx.toFixed(1)} U↓=${upperDownCurrentYpx.toFixed(1)} ` +
          `L→=${lowerRightCurrentXpx.toFixed(1)} L←=${lowerLeftCurrentXpx.toFixed(1)}`
        );
      }

      if (changed) rafId = requestAnimationFrame(animateStep);
      else rafId = null;
    }
    function triggerAnim(){ if (!rafId) animateStep(); }

    // Level → targets
    function recomputeTargets() {
      function decide(currTarget, ext, ret, drop) {
        if (ret && !ext) return 0;
        if (ext && !ret) return drop;
        if (!ext && !ret) return currTarget; // hold
        return 0; // both true -> fail-safe retract
      }

      const newRpull = decide(rPullerTargetYmm, bits.rPull.ext, bits.rPull.ret, PULLER_DROP_MM);
      const newLpull = decide(lPullerTargetYmm, bits.lPull.ext, bits.lPull.ret, PULLER_DROP_MM);
      const newRpis  = decide(rPistonTargetYmm, bits.rPis.ext,  bits.rPis.ret,  PUSHER_PISTON_DROP_MM);
      const newLpis  = decide(lPistonTargetYmm, bits.lPis.ext,  bits.lPis.ret,  PUSHER_PISTON_DROP_MM);
      const newRrel  = decide(rPusherRelTargetYmm, bits.rRel.ext, bits.rRel.ret, PUSHER_DROP_MM);
      const newLrel  = decide(lPusherRelTargetYmm, bits.lRel.ext, bits.lRel.ret, PUSHER_DROP_MM);

      let changed = false;
      if (newRpull !== rPullerTargetYmm) { rPullerTargetYmm = newRpull; console.error(`🎯 rightPuller=${newRpull}mm`); changed = true; }
      if (newLpull !== lPullerTargetYmm) { lPullerTargetYmm = newLpull; console.error(`🎯 leftPuller=${newLpull}mm`);  changed = true; }
      if (newRpis  !== rPistonTargetYmm) { rPistonTargetYmm = newRpis;  console.error(`🎯 rightPiston=${newRpis}mm`);  changed = true; }
      if (newLpis  !== lPistonTargetYmm) { lPistonTargetYmm = newLpis;  console.error(`🎯 leftPiston=${newLpis}mm`);   changed = true; }
      if (newRrel  !== rPusherRelTargetYmm) { rPusherRelTargetYmm = newRrel; console.error(`🎯 rightPusherREL=${newRrel}mm`); changed = true; }
      if (newLrel  !== lPusherRelTargetYmm) { lPusherRelTargetYmm = newLrel; console.error(`🎯 leftPusherREL=${newLrel}mm`);  changed = true; }

      if (changed) triggerAnim();
    }

    // React to scale changes
    const unScale = tools.onScale ? tools.onScale(() => {
      console.error("🔄 top_view scale update");
      triggerAnim();
    }) : null;

    // Resolve widgets
    console.error("🔎 top_view: waiting for widgets via shmi.onReady…");
    self.vars.cancelable = shmi.onReady({ controls: SELECTORS }, function(resolved){
      console.error("🎯 top_view: onReady resolved");
      Object.keys(SELECTORS).forEach(k=>{
        els[k] = resolved.controls[k]?.element || null;
        console.error(els[k] ? `✅ Widget resolved: ${k}` : `❌ Widget missing: ${k} (${SELECTORS[k]})`);
      });

      // Subscriptions
      const sub = (item, fn) => im.subscribe([item], (n,v)=>{ console.error(`📥 ${n}=${v}`); fn(v); });

      // Arm X
      self.vars.tkPos = sub(ITEM_POS, v=>{
        if (typeof v === "number") {
          armTargetXmm = v;
          triggerAnim();
        }
      });

      // Pullers
      self.vars.tRPExt = sub(ITEM_RPULL_EXT, v=>{ bits.rPull.ext = isTrue(v); recomputeTargets(); });
      self.vars.tRPRet = sub(ITEM_RPULL_RET, v=>{ bits.rPull.ret = isTrue(v); recomputeTargets(); });
      self.vars.tLPExt = sub(ITEM_LPULL_EXT, v=>{ bits.lPull.ext = isTrue(v); recomputeTargets(); });
      self.vars.tLPRet = sub(ITEM_LPULL_RET, v=>{ bits.lPull.ret = isTrue(v); recomputeTargets(); });

      // Pusher pistons
      self.vars.tRPisE = sub(ITEM_RPIS_EXT,  v=>{ bits.rPis.ext = isTrue(v);  recomputeTargets(); });
      self.vars.tRPisR = sub(ITEM_RPIS_RET,  v=>{ bits.rPis.ret = isTrue(v);  recomputeTargets(); });
      self.vars.tLPisE = sub(ITEM_LPIS_EXT,  v=>{ bits.lPis.ext = isTrue(v);  recomputeTargets(); });
      self.vars.tLPisR = sub(ITEM_LPIS_RET,  v=>{ bits.lPis.ret = isTrue(v);  recomputeTargets(); });

      // Pushers (relative)
      self.vars.tRPushE = sub(ITEM_RPUSH_EXT, v=>{ bits.rRel.ext = isTrue(v); recomputeTargets(); });
      self.vars.tRPushR = sub(ITEM_RPUSH_RET, v=>{ bits.rRel.ret = isTrue(v); recomputeTargets(); });
      self.vars.tLPushE = sub(ITEM_LPUSH_EXT, v=>{ bits.lRel.ext = isTrue(v); recomputeTargets(); });
      self.vars.tLPushR = sub(ITEM_LPUSH_RET, v=>{ bits.lRel.ret = isTrue(v); recomputeTargets(); });

      // Storage movers
      self.vars.tTopF = sub(ITEM_ST_TOP_FWD, v=>{
        const on = isTrue(v);
        upperUpTargetYmm = on ? -STORAGE_DELTA_MM : 0;
        console.error(`🎯 upper-up Y=${upperUpTargetYmm}mm`);
        triggerAnim();
      });

      self.vars.tTopB = sub(ITEM_ST_TOP_BWD, v=>{
        const on = isTrue(v);
        upperDownTargetYmm = on ? STORAGE_DELTA_MM : 0;
        console.error(`🎯 upper-down Y=${upperDownTargetYmm}mm`);
        triggerAnim();
      });

      self.vars.tBotR = sub(ITEM_ST_BOT_RIGHT, v=>{
        const on = isTrue(v);
        lowerRightTargetXmm = on ? STORAGE_DELTA_MM : 0;
        console.error(`🎯 lower-right X=${lowerRightTargetXmm}mm`);
        triggerAnim();
      });

      self.vars.tBotL = sub(ITEM_ST_BOT_LEFT, v=>{
        const on = isTrue(v);
        lowerLeftTargetXmm = on ? -STORAGE_DELTA_MM : 0;
        console.error(`🎯 lower-left X=${lowerLeftTargetXmm}mm`);
        triggerAnim();
      });
    });

    self.onDisable = function () {
      self.run = false;
      console.error("🛑 top_view disabled");
      for (const k in self.vars) {
        const t = self.vars[k];
        if (!t) continue;
        if (typeof t.unsubscribe === "function") t.unsubscribe();
        else if (typeof t.unlisten   === "function") t.unlisten();
      }
      if (self.vars.cancelable) self.vars.cancelable.cancel();
      if (rafId) cancelAnimationFrame(rafId);
      if (typeof unScale === "function") unScale();
    };
  };
})();
