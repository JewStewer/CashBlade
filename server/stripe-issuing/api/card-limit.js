const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// POST { cardId, limitCents } -> { id, limitCents }
// Sets the card's all-time spending cap to an absolute amount (not a delta).
module.exports = withCors(async (req, res) => {
    if (req.method !== 'POST') return res.status(405).json({ error: 'Method not allowed' });

    const { cardId, limitCents } = req.body || {};
    if (!cardId || limitCents == null || limitCents < 0) {
        return res.status(400).json({ error: 'cardId and a non-negative limitCents are required.' });
    }

    try {
        const card = await stripe.issuing.cards.update(cardId, {
            spending_controls: {
                spending_limits: [{ amount: limitCents, interval: 'all_time' }],
            },
        });
        const limit = (card.spending_controls?.spending_limits || []).find(l => l.interval === 'all_time');
        res.status(200).json({ id: card.id, limitCents: limit?.amount ?? 0 });
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});
