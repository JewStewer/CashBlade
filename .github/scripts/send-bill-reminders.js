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

const webpush = require('web-push');
const { loadEnv, makeSupabaseClient, sendToAllSubscriptions, isBillUnpaid } = require('./push-helpers');

const env = loadEnv(['SUPABASE_URL', 'SUPABASE_ANON_KEY', 'VAPID_PUBLIC_KEY', 'VAPID_PRIVATE_KEY'], 'Bill reminders not configured');
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
    // 1. Get the latest sync payload (bills live inside finance_sync)
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

    // 2. Find unpaid bills due within 3 days
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

    // 4. Send to all subscribed devices
    await sendToAllSubscriptions(webpush, supabase, notification);
}

main().catch(err => { console.error(err); process.exit(1); });
