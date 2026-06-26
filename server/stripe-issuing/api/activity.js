const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// GET ?cardId=...&limit=20 -> [{ id, merchant, amountCents, approved, status, createdAt }]
// Authorizations (not Transactions) are used because they include declined
// attempts too — Transactions only ever represent money that actually moved.
module.exports = withCors(async (req, res) => {
    if (req.method !== 'GET') return res.status(405).json({ error: 'Method not allowed' });

    const cardId = req.query?.cardId;
    if (!cardId) return res.status(400).json({ error: 'cardId is required.' });
    const limit = Math.min(parseInt(req.query?.limit, 10) || 20, 100);

    try {
        const authorizations = await stripe.issuing.authorizations.list({ card: cardId, limit });
        const activity = authorizations.data.map(a => ({
            id: a.id,
            merchant: a.merchant_data?.name || 'Unknown merchant',
            amountCents: a.amount,
            approved: a.approved,
            status: a.status,
            createdAt: new Date(a.created * 1000).toISOString(),
        }));
        res.status(200).json(activity);
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});
