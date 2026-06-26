const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// POST { cardholderId, limitCents, currency } -> { id, last4, expMonth, expYear, status, limitCents }
// GET  ?cardId=... -> same shape
module.exports = withCors(async (req, res) => {
    try {
        if (req.method === 'POST') {
            const { cardholderId, limitCents, currency } = req.body || {};
            if (!cardholderId || !limitCents || limitCents <= 0) {
                return res.status(400).json({ error: 'cardholderId and a positive limitCents are required.' });
            }
            const card = await stripe.issuing.cards.create({
                cardholder: cardholderId,
                currency: currency || 'usd',
                type: 'virtual',
                spending_controls: {
                    spending_limits: [{ amount: limitCents, interval: 'all_time' }],
                },
            });
            return res.status(200).json(toCardDto(card));
        }

        if (req.method === 'GET') {
            const cardId = req.query?.cardId;
            if (!cardId) return res.status(400).json({ error: 'cardId is required.' });
            const card = await stripe.issuing.cards.retrieve(cardId);
            return res.status(200).json(toCardDto(card));
        }

        res.status(405).json({ error: 'Method not allowed' });
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});

function toCardDto(card) {
    const limit = (card.spending_controls?.spending_limits || []).find(l => l.interval === 'all_time');
    return {
        id: card.id,
        last4: card.last4,
        expMonth: card.exp_month,
        expYear: card.exp_year,
        status: card.status,
        limitCents: limit?.amount ?? 0,
    };
}
