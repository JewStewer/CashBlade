const Stripe = require('stripe');

if (!process.env.STRIPE_SECRET_KEY) {
    throw new Error('STRIPE_SECRET_KEY is not set. Copy .env.example to .env and fill in your Stripe test secret key.');
}

module.exports = new Stripe(process.env.STRIPE_SECRET_KEY, {
    apiVersion: '2024-06-20',
});
