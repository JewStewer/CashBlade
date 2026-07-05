#!/usr/bin/env node
// Runs daily via GitHub Actions — reads bills from Supabase and sends web push
// to the device subscribed via the app's Settings → Notifications page.
//
// Required GitHub Actions secrets:
//   VAPID_PUBLIC_KEY   — hardcoded in the app (BGJ1oO8…)
//   VAPID_PRIVATE_KEY  — 4u7ZnkAQAZXVZUUZw5i84yPVTQ14WXvTG8qFItyxLYk
//   VAPID_EMAIL        — e.g. mailto:you@example.com
//   PUSH_SUBSCRIPTION  — JSON copied from Settings → Notifications in the app
//   SUPABASE_URL       — your Supabase project URL (for reading bill data)
//   SUPABASE_ANON_KEY  — Supabase anon/public key

const webpush = require('web-push');
const { loadEnv, makeSupabaseClient, sendToSubscription, isBillUnpaid } = require('./push-helpers');

const env = loadEnv(['VAPID_PUBLIC_KEY', 'VAPID_PRIVATE_KEY', 'PUSH_SUBSCRIPTION', 'SUPABASE_URL', 'SUPABASE_ANON_KEY'], 'Bill reminders not configured');
// VAPID_PUBLIC_KEY and VAPID_PRIVATE_KEY are resolved from VAPID_KEYS JSON secret by loadEnv
webpush.setVapidDetails(env.VAPID_EMAIL || 'mailto:admin@cashblade.app', env.VAPID_PUBLIC_KEY, env.VAPID_PRIVATE_KEY);
const supabase = makeSupabaseClient(env.SUPABASE_URL, env.SUPABASE_ANON_KEY);

function daysBetween(dateStr) {
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const target = new Date(dateStr);
    target.setHours(0, 0, 0, 0);
    return Math.round((target - today) / 86400000);
}

async function main() {
    const syncRows = await supabase.get('finance_sync?id=eq.main&select=payload');
    if (!syncRows.length) {
        console.log('No sync data found — skipping.');
        return;
    }

    let payload;
    try { payload = JSON.parse(syncRows[0].payload); }
    catch { console.error('Could not parse sync payload'); return; }

    const bills = payload.bills ?? [];
    const statuses = payload.billOccurrenceStatuses ?? [];

    const upcomingBills = bills.filter(b => {
        const days = daysBetween(b.dueDate);
        if (days < 0 || days > 3) return false;
        return isBillUnpaid(b, statuses);
    });

    if (!upcomingBills.length) {
        console.log('No upcoming bills in the next 3 days — nothing to send.');
        return;
    }

    console.log(`Found ${upcomingBills.length} upcoming bill(s):`, upcomingBills.map(b => b.name).join(', '));

    const billList = upcomingBills.map(b => {
        const days = daysBetween(b.dueDate);
        const when = days === 0 ? 'today' : days === 1 ? 'tomorrow' : `in ${days} days`;
        return `${b.name} (${when})`;
    }).join(', ');

    const notification = JSON.stringify({
        title: 'Evergrove — Bills Due Soon',
        body: `Upcoming: ${billList}`
    });

    await sendToSubscription(webpush, env.PUSH_SUBSCRIPTION, notification);
}

main().catch(err => { console.error(err); process.exit(1); });
