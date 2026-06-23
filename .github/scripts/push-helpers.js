// Shared Supabase REST + web-push plumbing used by both notification scripts
// in this folder. Plain CommonJS, no dependencies beyond what each caller
// already requires (`web-push`) — matches the rest of this folder's style.

function loadEnv(requiredKeys, title) {
    const values = { VAPID_EMAIL: process.env.VAPID_EMAIL };
    for (const key of requiredKeys) values[key] = process.env[key];

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

async function sendToAllSubscriptions(webpush, supabase, notificationJson) {
    const subscriptions = await supabase.get('push_subscriptions?select=id,subscription');
    if (!subscriptions.length) {
        console.log('No push subscriptions found — nothing to send.');
        return 0;
    }

    console.log(`Sending to ${subscriptions.length} subscription(s)…`);

    const results = await Promise.allSettled(
        subscriptions.map(async row => {
            let sub;
            try { sub = JSON.parse(row.subscription); }
            catch { console.warn(`Invalid subscription JSON for id=${row.id}`); return; }

            try {
                await webpush.sendNotification(sub, notificationJson);
                console.log(`✓ Sent to ${row.id}`);
            } catch (err) {
                if (err.statusCode === 410 || err.statusCode === 404) {
                    console.log(`Removing expired subscription ${row.id}`);
                    await supabase.delete(`push_subscriptions?id=eq.${row.id}`);
                } else {
                    console.error(`Failed for ${row.id}:`, err.message);
                }
            }
        })
    );

    const sent = results.filter(r => r.status === 'fulfilled').length;
    console.log(`Done. ${sent}/${subscriptions.length} notifications sent.`);
    return sent;
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

module.exports = { loadEnv, makeSupabaseClient, sendToAllSubscriptions, isBillUnpaid };
