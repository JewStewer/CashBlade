// Shared Supabase REST + web-push plumbing used by both notification scripts
// in this folder. Plain CommonJS, no dependencies beyond what each caller
// already requires (`web-push`) — matches the rest of this folder's style.

function loadEnv(requiredKeys, title) {
    // VAPID_KEYS is a JSON object {"pub":"...","priv":"..."} stored as a single
    // secret to work around GitHub secret scanning stripping raw base64url keys.
    const vapidKeys = (() => {
        try { return JSON.parse(process.env.VAPID_KEYS || '{}'); } catch { return {}; }
    })();

    const values = {
        VAPID_EMAIL: process.env.VAPID_EMAIL,
        VAPID_PUBLIC_KEY: vapidKeys.pub || process.env.VAPID_PUBLIC_KEY,
        VAPID_PRIVATE_KEY: vapidKeys.priv || process.env.VAPID_PRIVATE_KEY,
    };
    for (const key of requiredKeys) {
        if (!(key in values)) values[key] = process.env[key];
    }

    const missing = requiredKeys.filter(key => !values[key]);
    if (missing.length > 0) {
        console.error(`::error title=${title}::Missing required GitHub Actions secrets: ${missing.join(', ')}`);
        console.error('Add these secrets in GitHub: Settings > Secrets and variables > Actions > Repository secrets.');
        process.exit(1);
    }
    return values;
}

function makeSupabaseClient(supabaseUrl, supabaseAnonKey) {
    const baseUrl = supabaseUrl.replace(/\/+$/, '');
    const headers = {
        apikey: supabaseAnonKey,
        Authorization: `Bearer ${supabaseAnonKey}`,
        'Content-Type': 'application/json'
    };

    return {
        async get(path) {
            const res = await fetch(`${baseUrl}/rest/v1/${path}`, { headers });
            if (!res.ok) throw new Error(`Supabase GET ${path}: ${res.status} ${await res.text()}`);
            return res.json();
        },
        async upsert(path, body) {
            const res = await fetch(`${baseUrl}/rest/v1/${path}`, {
                method: 'POST',
                headers: { ...headers, Prefer: 'resolution=merge-duplicates' },
                body: JSON.stringify(body)
            });
            if (!res.ok) throw new Error(`Supabase POST ${path}: ${res.status} ${await res.text()}`);
        },
        async delete(path) {
            await fetch(`${baseUrl}/rest/v1/${path}`, { method: 'DELETE', headers });
        }
    };
}

async function sendToSubscription(webpush, subscriptionJson, notificationJson) {
    let sub;
    try { sub = typeof subscriptionJson === 'string' ? JSON.parse(subscriptionJson) : subscriptionJson; }
    catch { console.error('Invalid PUSH_SUBSCRIPTION JSON — re-copy it from Settings → Notifications in the app.'); return 0; }

    try {
        await webpush.sendNotification(sub, notificationJson);
        console.log('✓ Notification sent.');
        return 1;
    } catch (err) {
        if (err.statusCode === 410 || err.statusCode === 404) {
            console.error('Subscription expired. Re-enable notifications in Settings → Notifications and update the PUSH_SUBSCRIPTION secret.');
        } else {
            console.error('Send failed:', err.message);
        }
        return 0;
    }
}

// Mirrors the bill-paid check send-bill-reminders.js already relied on inline:
// a bill counts as paid if its own flag says so, or if the latest occurrence
// status (by due date) for that bill says so.
function isBillUnpaid(bill, statuses) {
    if (bill.isPaid) return false;
    const latestStatus = statuses
        .filter(s => s.billId === bill.id)
        .sort((a, z) => new Date(z.dueDate) - new Date(a.dueDate))[0];
    return !latestStatus?.isPaid;
}

module.exports = { loadEnv, makeSupabaseClient, sendToSubscription, isBillUnpaid };
