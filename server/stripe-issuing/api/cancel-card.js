const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// POST { cardId } -> { id, status }
module.exports = withCors(async (req, res) => {
    if (req.method !== 'POST') return res.status(405).json({ error: 'Method not allowed' });

    const { cardId } = req.body || {};
    if (!cardId) return res.status(400).json({ error: 'cardId is required.' });

    try {
        const card = await stripe.issuing.cards.update(cardId, { status: 'canceled' });
        res.status(200).json({ id: card.id, status: card.status });
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});
