#!/usr/bin/env node
// Runs daily via GitHub Actions — reads bills from Supabase and sends web push
// to all subscribed devices for bills due within the next 3 days.
//
// Required GitHub Actions secrets:
//   SUPABASE_URL      — your Supabase project URL
//   SUPABASE_ANON_KEY — Supabase anon/public key
//   VAPID_PUBLIC_KEY  — from: npx web-push generate-vapid-keys
//   VAPID_PRIVATE_KEY — from: npx web-push generate-vapid-keys
//   VAPID_EMAIL       — e.g. mailto:you@example.com

const {
    SUPABASE_URL,
    SUPABASE_ANON_KEY,
    VAPID_PUBLIC_KEY,
    VAPID_PRIVATE_KEY,
    VAPID_EMAIL
} = process.env;

const vapidEmail = VAPID_EMAIL || 'mailto:admin@cashblade.app';

const requiredSecrets = {
    SUPABASE_URL,
    SUPABASE_ANON_KEY,
    VAPID_PUBLIC_KEY,
    VAPID_PRIVATE_KEY
};

const missingSecrets = Object.entries(requiredSecrets)
    .filter(([, value]) => !value)
    .map(([name]) => name);

if (missingSecrets.length > 0) {
    const message = `Missing required GitHub Actions secrets: ${missingSecrets.join(', ')}`;
    console.error(`::error title=Bill reminders not configured::${message}`);
    console.error('Add these secrets in GitHub: Settings > Secrets and variables > Actions > Repository secrets.');
    process.exit(1);
}

const webpush = require('web-push');

webpush.setVapidDetails(vapidEmail, VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY);

const headers = {
    apikey: SUPABASE_ANON_KEY,
    Authorization: `Bearer ${SUPABASE_ANON_KEY}`,
    'Content-Type': 'application/json'
};

const baseUrl = SUPABASE_URL.replace(/\/+$/, '');

async function supabaseGet(path) {
    const res = await fetch(`${baseUrl}/rest/v1/${path}`, { headers });
    if (!res.ok) throw new Error(`Supabase ${path}: ${res.status} ${await res.text()}`);
    return res.json();
}

function daysBetween(dateStr) {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const target = new Date(dateStr);
    target.setHours(0, 0, 0, 0);
    return Math.round((target - today) / 86400000);
}

async function main() {
    // 1. Get the latest sync payload (bills live inside finance_sync)
    const syncRows = await supabaseGet('finance_sync?id=eq.main&select=payload');
    if (!syncRows.length) {
        console.log('No sync data found — skipping.');
        return;
    }

    let payload;
    try { payload = JSON.parse(syncRows[0].payload); }
    catch { console.error('Could not parse sync payload'); return; }

    const bills = payload.bills ?? [];
    const statuses = payload.billOccurrenceStatuses ?? [];
    const today = new Date(); today.setHours(0, 0, 0, 0);

    // 2. Find unpaid bills due within 3 days
    const upcomingBills = bills.filter(b => {
        if (b.isPaid) return false;
        const dueDate = new Date(b.dueDate); dueDate.setHours(0, 0, 0, 0);
        const days = daysBetween(b.dueDate);
        if (days < 0 || days > 3) return false;

        // Check BillOccurrenceStatuses — if the latest status for this bill marks it paid, skip
        const latestStatus = statuses
            .filter(s => s.billId === b.id)
            .sort((a, z) => new Date(z.dueDate) - new Date(a.dueDate))[0];
        if (latestStatus?.isPaid) return false;

        return true;
    });

    if (!upcomingBills.length) {
        console.log('No upcoming bills in the next 3 days — nothing to send.');
        return;
    }

    console.log(`Found ${upcomingBills.length} upcoming bill(s):`, upcomingBills.map(b => b.name).join(', '));

    // 3. Build notification payload
    const billList = upcomingBills.map(b => {
        const days = daysBetween(b.dueDate);
        const when = days === 0 ? 'today' : days === 1 ? 'tomorrow' : `in ${days} days`;
        return `${b.name} (${when})`;
    }).join(', ');

    const notification = JSON.stringify({
        title: 'Evergrove — Bills Due Soon',
        body: `Upcoming: ${billList}`
    });

    // 4. Get push subscriptions
    const subscriptions = await supabaseGet('push_subscriptions?select=id,subscription');
    if (!subscriptions.length) {
        console.log('No push subscriptions found — nothing to send.');
        return;
    }

    console.log(`Sending to ${subscriptions.length} subscription(s)…`);

    // 5. Send push to each subscriber
    const results = await Promise.allSettled(
        subscriptions.map(async row => {
            let sub;
            try { sub = JSON.parse(row.subscription); }
            catch { console.warn(`Invalid subscription JSON for id=${row.id}`); return; }

            try {
                await webpush.sendNotification(sub, notification);
                console.log(`✓ Sent to ${row.id}`);
            } catch (err) {
                if (err.statusCode === 410 || err.statusCode === 404) {
                    // Subscription expired — delete it
                    console.log(`Removing expired subscription ${row.id}`);
                    await fetch(`${baseUrl}/rest/v1/push_subscriptions?id=eq.${row.id}`, {
                        method: 'DELETE', headers
                    });
                } else {
                    console.error(`Failed for ${row.id}:`, err.message);
                }
            }
        })
    );

    const sent = results.filter(r => r.status === 'fulfilled').length;
    console.log(`Done. ${sent}/${subscriptions.length} notifications sent.`);
}

main().catch(err => { console.error(err); process.exit(1); });
