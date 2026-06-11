// Web Push subscription helpers — called from Blazor via JS interop

window.pushNotifications = {
    isSupported: () => ('Notification' in window),

    getPermission: () => ('Notification' in window ? Notification.permission : 'unsupported'),

    async requestPermission() {
        if (!('Notification' in window)) return 'unsupported';
        const result = await Notification.requestPermission();
        return result; // 'granted' | 'denied' | 'default'
    },

    async subscribe(vapidPublicKeyBase64) {
        try {
            const reg = await navigator.serviceWorker.ready;
            // Convert base64url VAPID public key to Uint8Array
            const key = urlBase64ToUint8Array(vapidPublicKeyBase64);
            const sub = await reg.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: key
            });
            // Return as JSON so Blazor can send to Supabase
            return JSON.stringify(sub.toJSON());
        } catch (e) {
            console.error('[push.js] subscribe failed', e);
            return null;
        }
    },

    async getSubscription() {
        try {
            const reg = await navigator.serviceWorker.ready;
            const sub = await reg.pushManager.getSubscription();
            return sub ? JSON.stringify(sub.toJSON()) : null;
        } catch (e) { return null; }
    },

    async unsubscribe() {
        try {
            const reg = await navigator.serviceWorker.ready;
            const sub = await reg.pushManager.getSubscription();
            if (sub) await sub.unsubscribe();
            return true;
        } catch (e) { return false; }
    },

    async showLocal(title, body, tag) {
        try {
            if (!('Notification' in window) || Notification.permission !== 'granted') return false;
            const options = {
                body,
                tag: tag || 'finora-local',
                icon: 'icons/icon-192.png',
                badge: 'icons/icon-192.png'
            };
            if ('serviceWorker' in navigator) {
                const reg = await navigator.serviceWorker.ready;
                await reg.showNotification(title, options);
            } else {
                new Notification(title, options);
            }
            return true;
        } catch (e) {
            console.error('[push.js] local notification failed', e);
            return false;
        }
    },

    wasLocalNotificationShown(key) {
        try { return localStorage.getItem('notification:' + key) === '1'; }
        catch { return false; }
    },

    markLocalNotificationShown(key) {
        try { localStorage.setItem('notification:' + key, '1'); }
        catch {}
    }
};

function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const rawData = atob(base64);
    return Uint8Array.from([...rawData].map(c => c.charCodeAt(0)));
}
