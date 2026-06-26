const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// POST { cardId, amountCents } -> { id, limitCents }
// "Loading funds" raises the card's all-time spending cap by amountCents.
// This does NOT move money at Stripe — Stripe Issuing spend is funded from
// your Stripe balance, not a per-card wallet. Make sure your Stripe balance
// (or linked bank funding source, depending on your Issuing setup) actually
// covers the new cap, or real purchases against it will be declined for
// insufficient funds even though the cap allows them.
module.exports = withCors(async (req, res) => {
    if (req.method !== 'POST') return res.status(405).json({ error: 'Method not allowed' });

    const { cardId, amountCents } = req.body || {};
    if (!cardId || !amountCents || amountCents <= 0) {
        return res.status(400).json({ error: 'cardId and a positive amountCents are required.' });
    }

    try {
        const card = await stripe.issuing.cards.retrieve(cardId);
        const currentLimit = (card.spending_controls?.spending_limits || []).find(l => l.interval === 'all_time');
        const newAmount = (currentLimit?.amount ?? 0) + amountCents;

        const updated = await stripe.issuing.cards.update(cardId, {
            spending_controls: {
                spending_limits: [{ amount: newAmount, interval: 'all_time' }],
            },
        });
        const limit = (updated.spending_controls?.spending_limits || []).find(l => l.interval === 'all_time');
        res.status(200).json({ id: updated.id, limitCents: limit?.amount ?? 0 });
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});
