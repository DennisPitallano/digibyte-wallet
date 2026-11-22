// The wallet's two optional chimes (payment received, send confirmed). Off unless the user turns Sounds on in Settings;
// SoundService decides whether to call in here at all. Files are self-hosted under /audio, so the content policy
// (default-src 'self') already covers them.
window.sounds = (function () {
    var files = { receive: 'audio/receive.mp3', send: 'audio/send.mp3' };
    var players = null;
    var unlocked = false;

    // Browsers only let a page play audio after the user has touched it, and some (iOS Safari) grant that per audio
    // element. A silent play inside a tap is what earns it, so a chime for a payment that arrives later can be heard.
    function unlock() {
        if (unlocked || !players) return;
        unlocked = true;
        Object.keys(players).forEach(function (name) {
            var a = players[name];
            try {
                a.muted = true;
                var p = a.play();
                // A real chime may be asked for in the same tap (the send overlay opens on a button press): leave that
                // one playing instead of cutting it off.
                var reset = function () {
                    if (a.__wanted) { a.muted = false; return; }
                    a.pause(); a.currentTime = 0; a.muted = false;
                };
                if (p && p.then) p.then(reset, function () { a.muted = false; unlocked = false; });
                else reset();
            } catch (e) { a.muted = false; unlocked = false; }
        });
    }

    function onGesture() {
        unlock();
        if (unlocked) {
            document.removeEventListener('pointerdown', onGesture, true);
            document.removeEventListener('keydown', onGesture, true);
        }
    }

    return {
        // Loads the files and waits for the first tap or key press. Safe to call more than once.
        arm: function () {
            if (players) return;
            try {
                players = {};
                Object.keys(files).forEach(function (name) {
                    var a = new Audio(files[name]);
                    a.preload = 'auto';
                    a.addEventListener('ended', function () { a.__wanted = false; });
                    players[name] = a;
                });
                document.addEventListener('pointerdown', onGesture, true);
                document.addEventListener('keydown', onGesture, true);
            } catch (e) { players = null; }
        },
        // Plays one chime. Best effort: a browser that refuses (no tap yet, no audio device) is ignored.
        play: function (name) {
            try {
                this.arm();
                var a = players && players[name];
                if (!a) return;
                a.__wanted = true;
                a.muted = false;
                a.currentTime = 0;
                var p = a.play();
                if (p && p.catch) p.catch(function () { a.__wanted = false; });
            } catch (e) { }
        }
    };
})();
