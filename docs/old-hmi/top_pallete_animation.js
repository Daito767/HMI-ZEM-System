(function () {
  const MODULE_NAME = "top_pallete_animation";
  const module = shmi.pkg(MODULE_NAME);

  // ---------- constants ----------
  const PALETTE_IDS = [0,1,2,3,4,5];
  const BASELINE_MM = 560;         // conveyor baseline (0 offset when distance == 560)
  const H_SHIFT_LEFT_MM  =  140;   // Right buffer → move left
  const H_SHIFT_RIGHT_MM = -140;   // Left buffer  → move right
  const PICKED_Y_MM      = 70;     // picked vertical target
  const ARM_X_BASELINE   = 266;    // picked x baseline (mm)
  const STACK_STEP_MM    = 120;    // stack pitch
  const FIRST_LEFT_RIGHT_Y_MM = 70;
  const LEFT_RIGHT_Y_AFTER0   = 125; // base + (idx-1)*120 for idx>0

  const SMOOTHING_SPEED = 0.15;
  const MIN_DELTA_PX    = 0.1;

  // ---------- items ----------
  const ITEM_HI = "Distance_Center_1";
  const ITEM_LO = "Distance_Center_2";
  const ITEM_PICKED = "PickedPalletID";
  const ITEM_ARMPOS = "ArmPosition";

  // Conveyor buffer ids (0..3)
  const ITEMS_CONV = [
    "ConveyorPalletBuffer._pallets_id[0]",
    "ConveyorPalletBuffer._pallets_id[1]",
    "ConveyorPalletBuffer._pallets_id[2]",
    "ConveyorPalletBuffer._pallets_id[3]"
  ];

  // Left buffer ids (0..3)
  const ITEMS_LEFT = [
    "LeftPalletBuffer._pallets_id[0]",
    "LeftPalletBuffer._pallets_id[1]",
    "LeftPalletBuffer._pallets_id[2]",
    "LeftPalletBuffer._pallets_id[3]"
  ];

  // Right buffer ids (0..3)
  const ITEMS_RIGHT = [
    "RightPalletBuffer._pallets_id[0]",
    "RightPalletBuffer._pallets_id[1]",
    "RightPalletBuffer._pallets_id[2]",
    "RightPalletBuffer._pallets_id[3]"
  ];

  // ---------- selectors ----------
  const SELECTORS = {
    // six palette containers
    p0: ".pallete-0",
    p1: ".pallete-1",
    p2: ".pallete-2",
    p3: ".pallete-3",
    p4: ".pallete-4",
    p5: ".pallete-5",
    // (optional) container if you want to compute local scale when tools missing
    container: ".top-view-container"
  };

  // ---------- shared tools (with safe fallback) ----------
  const tools = (function () {
    const t = shmi.pkg("custom.tools") || {};
    const ok = typeof t.mmToPx==="function" && typeof t.onScale==="function" && typeof t.getPxPerMm==="function";
    if (ok) { console.error("🔗 top_pallete_animation using custom.tools"); return t; }

    console.error("⚠️ custom.tools missing — using local mm→px fallback");
    // minimal local converter
    let pxPerMm = 1;
    const listeners = new Set();
    const SVG_W = 750, SVG_H = 900;
    function computePxPerMm(containerEl){
      const r = SVG_W/SVG_H;
      const rect = containerEl?.getBoundingClientRect?.();
      if (rect && rect.width>0 && rect.height>0){
        return (rect.width/rect.height <= r) ? (rect.width/SVG_W) : (rect.height/SVG_H);
      }
      const sr = window.innerWidth/window.innerHeight;
      return (sr <= r) ? (window.innerWidth/SVG_W) : (window.innerHeight/SVG_H);
    }
    const api = {
      mmToPx: mm => mm*pxPerMm,
      getPxPerMm: () => pxPerMm,
      onScale: cb => { listeners.add(cb); try{cb(pxPerMm);}catch{} return ()=>listeners.delete(cb); },
      _set: v => { pxPerMm = v; for (const cb of listeners) try{cb(v);}catch{}; }
    };
    shmi.onReady({controls:{container:SELECTORS.container}}, (resolved)=>{
      const el = resolved.controls.container?.element || null;
      api._set(computePxPerMm(el));
      window.addEventListener("resize", ()=> api._set(computePxPerMm(el)));
    });
    return api;
  })();

  // ---------- helpers ----------
  const isValidId = v => Number.isInteger(v) && v >= 0 && v <= 5;

  // Placement computation
  function conveyorYmm(distMm, idx){                 // “same logic as before”
    const composite = distMm + (STACK_STEP_MM * idx);
    return (BASELINE_MM - composite);                // +down if distance < baseline
  }
  function leftRightYmm(idx){                        // fixed stack distances
    return (idx === 0) ? BASELINE_MM - FIRST_LEFT_RIGHT_Y_MM
                       : BASELINE_MM -  ((idx - 1) * STACK_STEP_MM + LEFT_RIGHT_Y_AFTER0);
  }

  // ---------- run ----------
  module.run = function (self) {
    console.error("✅ top_pallete_animation run() started.");
    const im = shmi.requires("visuals.session.ItemManager");
    self.vars = self.vars || {};

    // DOM elements by palette id
    const els = {};
    // Per-palette animation state (targets in mm, currents in px)
    const state = {};
    for (const id of PALETTE_IDS){
      state[id] = { tx:0, ty:0, x:0, y:0 }; // tx/ty in mm; x/y in px
    }

    // live signals
    let hi = 0, lo = 0, armPos = ARM_X_BASELINE, picked = -1;
    const conv = [-1,-1,-1,-1], left = [-1,-1,-1,-1], right = [-1,-1,-1,-1];

    function computeDistanceMm(){ return (hi<<8) + (lo & 0xFF); }

    // Build reverse map: which buffer/index holds a given palette id
    function locatePalettes(){
      const where = {}; // id -> {type, idx}
      for (let i=0;i<4;i++){
        if (isValidId(conv[i]))  where[conv[i]]  = { type:"conv",  idx:i };
        if (isValidId(left[i]))  where[left[i]]  = { type:"left",  idx:i };
        if (isValidId(right[i])) where[right[i]] = { type:"right", idx:i };
      }
      if (isValidId(picked)) where[picked] = { type:"picked", idx:0 }; // highest priority
      return where;
    }

    function recomputeTargets(){
      const dist = computeDistanceMm();
      const where = locatePalettes();

      for (const id of PALETTE_IDS){
        let tx = 0, ty = 0; // mm
        const loc = where[id];

        if (loc && loc.type === "picked") {
          ty = BASELINE_MM - PICKED_Y_MM;
          tx = (armPos - ARM_X_BASELINE);
        } else if (loc && loc.type === "conv") {
          ty = conveyorYmm(dist, loc.idx);
          tx = 0;
        } else if (loc && loc.type === "left") {
          ty = leftRightYmm(loc.idx);
          tx = H_SHIFT_RIGHT_MM; // move to the right
        } else if (loc && loc.type === "right") {
          ty = leftRightYmm(loc.idx);
          tx = H_SHIFT_LEFT_MM;  // move to the left
        } else {
          // not present → default spot
          tx = 0; ty = 0;
        }

        if (tx !== state[id].tx || ty !== state[id].ty){
          state[id].tx = tx;
          state[id].ty = ty;
        }
      }
      trigger();
    }

    function applyTransform(id){
      const el = els[id];
      if (!el) return;
      el.style.transform = `translate(${state[id].x}px, ${state[id].y}px)`;
    }

    let raf = null;
    function step(){
      let anyChange = false;
      for (const id of PALETTE_IDS){
        const targetX = tools.mmToPx(state[id].tx);
        const targetY = tools.mmToPx(state[id].ty);
        const dx = targetX - state[id].x;
        const dy = targetY - state[id].y;
        const cx = Math.abs(dx) > MIN_DELTA_PX;
        const cy = Math.abs(dy) > MIN_DELTA_PX;
        if (cx) state[id].x += dx * SMOOTHING_SPEED;
        if (cy) state[id].y += dy * SMOOTHING_SPEED;
        if (cx || cy) anyChange = true;
        applyTransform(id);
      }
      if (anyChange) raf = requestAnimationFrame(step);
      else raf = null;
    }
    function trigger(){ if (!raf) raf = requestAnimationFrame(step); }

    // resolve DOM
    const controlsMap = {
      p0: SELECTORS.p0, p1: SELECTORS.p1, p2: SELECTORS.p2,
      p3: SELECTORS.p3, p4: SELECTORS.p4, p5: SELECTORS.p5
    };
    self.vars.cancelable = shmi.onReady({ controls: controlsMap }, (resolved)=>{
      for (let i=0;i<6;i++){
        const key = "p"+i;
        els[i] = resolved.controls[key]?.element || null;
        console.error(els[i] ? `✅ palette ${i} resolved` : `❌ palette ${i} missing`);
      }

      // subscriptions
      const sub = (item, fn) => im.subscribe([item], (n,v)=>{ fn(v); });

      // distance bytes
      self.vars.tHi = sub(ITEM_HI, v=>{ hi = (v|0) & 0xFF;  recomputeTargets(); });
      self.vars.tLo = sub(ITEM_LO, v=>{ lo = (v|0) & 0xFF;  recomputeTargets(); });

      // buffers
      self.vars.tConv = ITEMS_CONV.map((it,idx)=> sub(it, v=>{ conv[idx] = (v|0); recomputeTargets(); }));
      self.vars.tLeft = ITEMS_LEFT.map((it,idx)=> sub(it, v=>{ left[idx] = (v|0); recomputeTargets(); }));
      self.vars.tRight= ITEMS_RIGHT.map((it,idx)=> sub(it, v=>{ right[idx]= (v|0); recomputeTargets(); }));

      // picked + arm
      self.vars.tPicked = sub(ITEM_PICKED, v=>{ picked = (v|0); recomputeTargets(); });
      self.vars.tArmPos = sub(ITEM_ARMPOS, v=>{
        if (typeof v === "number") { armPos = v; recomputeTargets(); }
      });

      // react to scale
      self.vars.unScale = tools.onScale(()=> trigger());
    });

    self.onDisable = function () {
      // best-effort cleanup
      for (const k in self.vars) {
        const t = self.vars[k];
        if (!t) continue;
        if (Array.isArray(t)) { t.forEach(tok => tok?.unsubscribe?.() || tok?.unlisten?.()); }
        else if (typeof t?.unsubscribe==="function") t.unsubscribe();
        else if (typeof t?.unlisten==="function") t.unlisten();
      }
      if (self.vars.cancelable) self.vars.cancelable.cancel();
      if (typeof self.vars.unScale === "function") self.vars.unScale();
      if (raf) cancelAnimationFrame(raf);
    };
  };
})();
