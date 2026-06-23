#!/usr/bin/env node
// Runs daily via GitHub Actions (same cron as send-bill-reminders.js) — scans
// the synced financial data for things worth surfacing proactively, without
// anyone having to open the app and notice them:
//   - balance is overdrawn, or projected to go negative once bills are paid
//   - this week's spending is well above the recent average pace
//   - a recurring charge looks like a new subscription that isn't a tracked Bill yet
//   - a tracked bill's amount changed since the last time it was seen
//
// Dedupe/cooldown state lives in the `notification_state` table (one row,
// id='main') so the same condition doesn't re-notify every single day.
//
// Required GitHub Actions secrets: same as send-bill-reminders.js
// (SUPABASE_URL, SUPABASE_ANON_KEY, VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY, VAPID_EMAIL)

const webpush = require('web-push');
const { loadEnv, makeSupabaseClient, sendToAllSubscriptions, isBillUnpaid } = require('./push-helpers');

const env = loadEnv(['SUPABASE_URL', 'SUPABASE_ANON_KEY', 'VAPID_PUBLIC_KEY', 'VAPID_PRIVATE_KEY'], 'Smart alerts not configured');
webpush.setVapidDetails(env.VAPID_EMAIL || 'mailto:admin@cashblade.app', env.VAPID_PUBLIC_KEY, env.VAPID_PRIVATE_KEY);
const supabase = makeSupabaseClient(env.SUPABASE_URL, env.SUPABASE_ANON_KEY);

const LOW_BALANCE_COOLDOWN_DAYS = 5;
const PACE_THRESHOLD = 1.5; // this week's spend vs trailing 4-week average

// ── Transaction helpers — mirror Finora.Web/Services/AppState.cs + TransactionClassification.cs ──

function isInternalMovement(t) {
    const cat = (t.categoryName || '').trim();
    if (cat === 'Transfer' || cat === 'Opening Balance' || cat === 'Balance Adjustment') return true;
    if (t.transferId && t.transferId !== '00000000-0000-0000-0000-000000000000') return true;
    const desc = t.description || '';
    if (/^transfer /i.test(desc.trim())) return true;
    if (/ transfer /i.test(desc)) return true;
    if (/\bcover(?:ed)? (?:from|to)\b/i.test(desc)) return true;
    return false;
}

function normalizeRecurringDescription(description) {
    const cleaned = (description || '').trim();
    const idx = cleaned.indexOf(' - ');
    return idx > 0 ? cleaned.slice(0, idx) : cleaned;
}

function median(numbers) {
    const sorted = [...numbers].sort((a, b) => a - b);
    return sorted[Math.floor(sorted.length / 2)];
}

function getRecurringFrequency(medianGapDays) {
    if (medianGapDays >= 5 && medianGapDays <= 9) return { frequency: 'Weekly', days: 7 };
    if (medianGapDays >= 12 && medianGapDays <= 17) return { frequency: 'Fortnightly', days: 14 };
    if (medianGapDays >= 26 && medianGapDays <= 35) return { frequency: 'Monthly', days: 30 };
    if (medianGapDays >= 80 && medianGapDays <= 100) return { frequency: 'Quarterly', days: 91 };
    if (medianGapDays >= 350 && medianGapDays <= 380) return { frequency: 'Yearly', days: 365 };
    return { frequency: '', days: 0 };
}

function buildRecurringPayment(sortedTransactions) {
    const gaps = [];
    for (let i = 1; i < sortedTransactions.length; i++) {
        const gap = (new Date(sortedTransactions[i].date) - new Date(sortedTransactions[i - 1].date)) / 86400000;
        if (gap > 0) gaps.push(gap);
    }
    if (gaps.length === 0) return null;

    const { frequency, days } = getRecurringFrequency(median(gaps));
    if (days === 0) return null;

    const last = sortedTransactions[sortedTransactions.length - 1];
    const amounts = sortedTransactions.map(t => Math.abs(t.amountCents) / 100);
    const averageAmount = Math.round((amounts.reduce((a, b) => a + b, 0) / amounts.length) * 100) / 100;

    return {
        name: normalizeRecurringDescription(last.description),
        averageAmount,
        frequency
    };
}

function getRecurringPayments(transactions, bills, ignoredNames) {
    const ignored = new Set(ignoredNames.map(n => n.toLowerCase()));
    const groups = new Map();
    for (const t of transactions) {
        if (t.amountCents >= 0 || isInternalMovement(t) || !(t.description || '').trim()) continue;
        const key = normalizeRecurringDescription(t.description);
        if (!groups.has(key)) groups.set(key, []);
        groups.get(key).push(t);
    }

    const result = [];
    for (const group of groups.values()) {
        if (group.length < 2) continue;
        const sorted = [...group].sort((a, b) => new Date(a.date) - new Date(b.date));
        const built = buildRecurringPayment(sorted);
        if (!built || ignored.has(built.name.toLowerCase())) continue;
        built.isAlreadyBill = bills.some(b => normalizeRecurringDescription(b.name).toLowerCase() === built.name.toLowerCase());
        result.push(built);
    }
    return result;
}

function frequencyShort(frequency) {
    switch (frequency) {
        case 'Weekly': return 'wk';
        case 'Fortnightly': return 'fortnight';
        case 'Monthly': return 'mo';
        case 'Quarterly': return 'quarter';
        case 'Yearly': return 'yr';
        default: return frequency.toLowerCase();
    }
}

function mondayOf(date) {
    const d = new Date(date);
    d.setHours(0, 0, 0, 0);
    const dow = d.getDay();
    d.setDate(d.getDate() - (dow === 0 ? 6 : dow - 1));
    return d;
}

function weekSpending(transactions, weeksAgo) {
    const from = new Date(mondayOf(new Date()).getTime() - weeksAgo * 7 * 86400000);
    const to = new Date(from.getTime() + 6 * 86400000);
    return transactions
        .filter(t => {
            const d = new Date(t.date);
            return d >= from && d <= to && t.amountCents < 0 && !isInternalMovement(t);
        })
        .reduce((sum, t) => sum + Math.abs(t.amountCents), 0) / 100;
}

function daysSince(dateStr) {
    return dateStr ? (Date.now() - new Date(dateStr).getTime()) / 86400000 : Infinity;
}

// ── Checks — each mutates state.sentAlerts and pushes a message when it fires ──

function checkLowBalance(payload, state, alerts) {
    const transactions = payload.transactions ?? [];
    const bills = payload.bills ?? [];
    const statuses = payload.billOccurrenceStatuses ?? [];
    const settings = payload.appSettings ?? [];
    const today0 = new Date(); today0.setHours(0, 0, 0, 0);
    const todayStr = today0.toISOString().slice(0, 10);

    const totalBalance = transactions.reduce((sum, t) => sum + t.amountCents, 0) / 100;

    if (totalBalance < 0 && daysSince(state.sentAlerts.low_balance) >= LOW_BALANCE_COOLDOWN_DAYS) {
        state.sentAlerts.low_balance = todayStr;
        alerts.push(`Your balance is overdrawn: $${totalBalance.toFixed(2)}.`);
    }

    const nextPayDateStr = settings.find(s => s.key === 'NextPayDate')?.value;
    const nextPayDate = nextPayDateStr ? new Date(nextPayDateStr) : today0;
    const payEnd = nextPayDate >= today0 ? nextPayDate : new Date(today0.getTime() + 14 * 86400000);

    // Uses raw dueDate, not AppState's EffectiveDueDate (client-only, never synced) —
    // same simplification send-bill-reminders.js already relies on.
    const billsTotal = bills
        .filter(b => isBillUnpaid(b, statuses) && new Date(b.dueDate) <= payEnd)
        .reduce((sum, b) => sum + b.amountCents, 0) / 100;
    const afterBills = totalBalance - billsTotal;

    if (totalBalance >= 0 && afterBills < 0 && daysSince(state.sentAlerts.low_balance_forecast) >= LOW_BALANCE_COOLDOWN_DAYS) {
        state.sentAlerts.low_balance_forecast = todayStr;
        alerts.push(`Forecast: balance may drop to $${afterBills.toFixed(2)} once upcoming bills are paid.`);
    }
}

function checkSpendingPace(payload, state, alerts) {
    if (new Date().getDay() === 1) return; // Monday — not enough data for this week yet

    const transactions = payload.transactions ?? [];
    const thisWeek = weekSpending(transactions, 0);
    const priorWeeks = [1, 2, 3, 4].map(w => weekSpending(transactions, w));
    const avg = priorWeeks.reduce((a, b) => a + b, 0) / priorWeeks.length;
    if (avg <= 0 || thisWeek <= avg * PACE_THRESHOLD) return;

    const key = `pace:${mondayOf(new Date()).toISOString().slice(0, 10)}`;
    if (state.sentAlerts[key]) return; // already alerted for this week

    state.sentAlerts[key] = new Date().toISOString().slice(0, 10);
    alerts.push(`Spending pace is up — $${thisWeek.toFixed(0)} this week vs your ~$${avg.toFixed(0)} usual.`);
}

function checkNewSubscriptions(payload, state, alerts) {
    const transactions = payload.transactions ?? [];
    const bills = payload.bills ?? [];
    const settings = payload.appSettings ?? [];

    let ignored = [];
    const ignoredJson = settings.find(s => s.key === 'IgnoredSubscriptions')?.value;
    if (ignoredJson) {
        try { ignored = JSON.parse(ignoredJson); } catch { /* malformed setting — treat as none ignored */ }
    }

    for (const sub of getRecurringPayments(transactions, bills, ignored)) {
        if (sub.isAlreadyBill) continue;
        const key = `subscription:${sub.name.toLowerCase()}`;
        if (state.sentAlerts[key]) continue; // one-shot per distinct name, ever

        state.sentAlerts[key] = new Date().toISOString().slice(0, 10);
        alerts.push(`New subscription detected: ${sub.name} ~$${sub.averageAmount.toFixed(2)}/${frequencyShort(sub.frequency)}.`);
    }
}

function checkBillAmountChanged(payload, state, alerts) {
    for (const bill of payload.bills ?? []) {
        const key = `billAmount:${bill.id}`;
        const previous = state.sentAlerts[key];
        if (previous === undefined) {
            state.sentAlerts[key] = bill.amountCents; // first time seeing it — record baseline only
            continue;
        }
        if (previous !== bill.amountCents) {
            state.sentAlerts[key] = bill.amountCents;
            alerts.push(`${bill.name} changed from $${(previous / 100).toFixed(2)} to $${(bill.amountCents / 100).toFixed(2)}.`);
        }
    }
}

// ── Main ──

async function loadState() {
    const rows = await supabase.get('notification_state?id=eq.main&select=state');
    const raw = rows[0]?.state;
    const state = typeof raw === 'string' ? JSON.parse(raw) : (raw ?? {});
    if (!state.sentAlerts) state.sentAlerts = {};
    return state;
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

    const state = await loadState();
    const alerts = [];

    checkLowBalance(payload, state, alerts);
    checkSpendingPace(payload, state, alerts);
    checkNewSubscriptions(payload, state, alerts);
    checkBillAmountChanged(payload, state, alerts);

    // Always persist — bill-amount baselines get recorded even on runs with no alerts.
    await supabase.upsert('notification_state', { id: 'main', state, updated_at: new Date().toISOString() });

    if (!alerts.length) {
        console.log('No new alerts.');
        return;
    }

    console.log(`Sending ${alerts.length} alert(s):`, alerts);
    const notification = JSON.stringify({ title: 'Evergrove — Smart Alerts', body: alerts.join('\n') });
    await sendToAllSubscriptions(webpush, supabase, notification);
}

main().catch(err => { console.error(err); process.exit(1); });
