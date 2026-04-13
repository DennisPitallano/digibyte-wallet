// Visibility change detection for session timeout hardening.
// Locks the wallet when the tab/app is hidden (minimized, switched away).
window.visibilityLock = {
    _dotnetRef: null,
    _lockOnHidden: false,

    init: function (dotnetRef, lockOnHidden) {
        this._dotnetRef = dotnetRef;
        this._lockOnHidden = lockOnHidden;

        document.addEventListener('visibilitychange', this._onVisibilityChange.bind(this));
    },

    setLockOnHidden: function (lockOnHidden) {
        this._lockOnHidden = lockOnHidden;
    },

    _onVisibilityChange: function () {
        if (document.visibilityState === 'hidden' && this._lockOnHidden && this._dotnetRef) {
            this._dotnetRef.invokeMethodAsync('OnTabHidden');
        }
    },

    dispose: function () {
        document.removeEventListener('visibilitychange', this._onVisibilityChange.bind(this));
        this._dotnetRef = null;
    }
};
