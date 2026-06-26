const stripe = require('../lib/stripe');

// Stripe webhook receiver. Requires the RAW request body for signature
// verification, so this is wired up separately from the other (JSON-parsed)
// endpoints — see server.js (Express) or vercel.json (bodyParser: false).
//
// We rely on Stripe's automatic spending_controls to approve/decline in
// real time, so this endpoint doesn't need to respond synchronously to
// issuing_authorization.request — it just logs settled activity. Wire in
// your own notification/sync logic where marked below.
module.exports.config = { api: { bodyParser: false } };

module.exports = async (req, res) => {
    if (req.method !== 'POST') return res.status(405).json({ error: 'Method not allowed' });

    const signature = req.headers['stripe-signature'];
    let event;
    try {
        const rawBody = await readRawBody(req);
        event = stripe.webhooks.constructEvent(rawBody, signature, process.env.STRIPE_WEBHOOK_SECRET);
    } catch (err) {
        return res.status(400).json({ error: `Webhook signature verification failed: ${err.message}` });
    }

    switch (event.type) {
        case 'issuing_authorization.created':
        case 'issuing_transaction.created':
            // TODO: push a notification to the app, or persist for an
            // activity feed that doesn't depend on polling Stripe directly.
            console.log(event.type, event.data.object.id);
            break;
        default:
            break;
    }

    res.status(200).json({ received: true });
};

function readRawBody(req) {
    return new Promise((resolve, reject) => {
        const chunks = [];
        req.on('data', chunk => chunks.push(chunk));
        req.on('end', () => resolve(Buffer.concat(chunks)));
        req.on('error', reject);
    });
}
