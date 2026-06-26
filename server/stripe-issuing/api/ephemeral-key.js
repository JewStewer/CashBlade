const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// POST { cardId, apiVersion } -> { ephemeralKeySecret }
// Used by Stripe.js Issuing Elements in the browser to reveal the full PAN/CVC
// for a virtual card without the card number ever passing through our server.
module.exports = withCors(async (req, res) => {
    if (req.method !== 'POST') return res.status(405).json({ error: 'Method not allowed' });

    const { cardId, apiVersion } = req.body || {};
    if (!cardId) return res.status(400).json({ error: 'cardId is required.' });

    try {
        const key = await stripe.ephemeralKeys.create(
            { issuing_card: cardId },
            { apiVersion: apiVersion || '2024-06-20' }
        );
        res.status(200).json({ ephemeralKeySecret: key.secret });
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});
