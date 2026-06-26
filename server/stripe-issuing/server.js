// Express entry point for deploying this as a regular Node server (Render,
// Railway, Fly.io, etc.) instead of Vercel serverless functions. Each file
// in api/ is a plain (req, res) handler so it works unchanged either way.
require('dotenv').config();
const express = require('express');

const app = express();

// The webhook route needs the raw body for Stripe signature verification,
// so it must be mounted BEFORE the global express.json() middleware.
app.post('/api/webhook', require('./api/webhook'));

app.use(express.json());

app.post('/api/cardholder', require('./api/cardholder'));
app.all('/api/card', require('./api/card'));
app.post('/api/card-limit', require('./api/card-limit'));
app.post('/api/topup', require('./api/topup'));
app.post('/api/ephemeral-key', require('./api/ephemeral-key'));
app.get('/api/activity', require('./api/activity'));
app.post('/api/cancel-card', require('./api/cancel-card'));

app.get('/api/ping', (req, res) => res.json({ ok: true, time: new Date().toISOString() }));

const port = process.env.PORT || 4242;
app.listen(port, () => console.log(`Stripe Issuing backend listening on :${port}`));
