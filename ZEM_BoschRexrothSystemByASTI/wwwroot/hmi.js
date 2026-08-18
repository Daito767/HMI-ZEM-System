// The PLC has no watchdog on the OPC UA session. If the window loses focus, gets hidden or is
// closed while a level flag is up, nothing in the PLC notices - so the HMI drops every command flag
// on each of those events.
// The audible alarm. No audio file: two beeps drawn from an oscillator, so it sounds the same on
// Android and on Windows and depends on nothing that can be lost when packaging.
window.hmiAlarm = (function () {
    const HZ = 880;              // high enough to carry over the noise of a shop floor
    const BEEP = 0.18;           // seconds
    const GAP = 0.26;            // between the two beeps of one group
    const EVERY = 1500;          // ms between groups
    const VOLUME = 0.25;

    let context = null;
    let timer = null;
    let sounding = false;

    // A page is not allowed to make a sound before someone has touched it - that is the browser
    // rule, and the WebView on the tablet keeps it too. So the first touch anywhere opens the sound
    // up, and from there on the alarm can speak by itself.
    function wake() {
        if (!context) {
            const Ctor = window.AudioContext || window.webkitAudioContext;
            if (!Ctor) return null;
            context = new Ctor();
        }

        if (context.state === 'suspended') context.resume();
        return context;
    }

    // The gate ramps open and closed: cut square, what you hear is a click, not a beep.
    function beep(at) {
        const osc = context.createOscillator();
        const gain = context.createGain();

        osc.type = 'square';
        osc.frequency.value = HZ;

        gain.gain.setValueAtTime(0.0001, at);
        gain.gain.exponentialRampToValueAtTime(VOLUME, at + 0.01);
        gain.gain.setValueAtTime(VOLUME, at + BEEP - 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, at + BEEP);

        osc.connect(gain).connect(context.destination);
        osc.start(at);
        osc.stop(at + BEEP + 0.02);
    }

    function group() {
        const ctx = wake();
        if (!ctx || ctx.state !== 'running') return;

        beep(ctx.currentTime);
        beep(ctx.currentTime + GAP);
    }

    return {
        listen: function () {
            const open = () => wake();
            window.addEventListener('pointerdown', open, { passive: true });
            window.addEventListener('keydown', open);
        },

        set: function (on) {
            if (on === sounding) return;
            sounding = on;

            if (timer) { clearInterval(timer); timer = null; }
            if (!on) return;

            group();
            timer = setInterval(group, EVERY);
        }
    };
})();

window.hmiSafety = {
    register: function (dotNetRef) {
        const release = () => {
            try { dotNetRef.invokeMethodAsync('ReleaseAllFlags'); } catch { /* going away anyway */ }
        };

        window.addEventListener('blur', release);
        window.addEventListener('pagehide', release);
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) release();
        });
    }
};
