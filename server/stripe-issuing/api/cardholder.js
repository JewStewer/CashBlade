const stripe = require('../lib/stripe');
const { withCors } = require('../lib/cors');

// POST { name, email, phoneNumber, address: { line1, city, state, postalCode, country } }
// -> { id }
module.exports = withCors(async (req, res) => {
    if (req.method !== 'POST') return res.status(405).json({ error: 'Method not allowed' });

    const { name, email, phoneNumber, address } = req.body || {};
    if (!name || !email || !address?.line1 || !address?.city || !address?.postalCode || !address?.country) {
        return res.status(400).json({ error: 'name, email, and a full billing address are required.' });
    }

    try {
        const cardholder = await stripe.issuing.cardholders.create({
            type: 'individual',
            name,
            email,
            phone_number: phoneNumber || undefined,
            billing: {
                address: {
                    line1: address.line1,
                    city: address.city,
                    state: address.state || '',
                    postal_code: address.postalCode,
                    country: address.country,
                },
            },
        });
        res.status(200).json({ id: cardholder.id, status: cardholder.status });
    } catch (err) {
        res.status(400).json({ error: err.message });
    }
});
