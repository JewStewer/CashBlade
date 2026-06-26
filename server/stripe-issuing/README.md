# Evergrove Stripe Issuing backend

A small stateless API that lets the Evergrove app create a real Stripe
Issuing virtual card, raise its spending cap ("load funds"), and read back
real card activity ("spend"). Stripe is the system of record — this backend
never stores card data itself; Evergrove keeps the cardholder/card IDs it
gets back, the same way it persists everything else (in IndexedDB).

## Before you start

1. **Apply for Stripe Issuing.** It is not enabled by default on a Stripe
   account — request access from your Stripe Dashboard
   (Issuing → Get started) and wait for approval. Test mode usually works
   immediately even before live approval, so you can build against it now.
2. Get your **test secret key** (`sk_test_...`) from the Dashboard.
3. Decide where actual spending will be funded from. Stripe Issuing draws
   from your Stripe balance (or a linked funding source, depending on how
   your account is configured) — *raising the cap in this app does not move
   money into that balance.* Keep that balance topped up yourself, or real
   purchases will be declined for insufficient funds even though the cap
   allows them.

## Endpoints

| Method | Path              | Body / Query                          | Purpose |
|--------|-------------------|----------------------------------------|---------|
| POST   | `/api/cardholder` | `{ name, email, phoneNumber, address }` | Create a Stripe Issuing Cardholder |
| POST   | `/api/card`       | `{ cardholderId, limitCents, currency }` | Create a virtual card with a spending cap |
| GET    | `/api/card`       | `?cardId=`                              | Read back a card's status/limit/last4 |
| POST   | `/api/card-limit` | `{ cardId, limitCents }`                | Set the cap to an absolute amount |
| POST   | `/api/topup`      | `{ cardId, amountCents }`               | Raise the cap by an amount ("load funds") |
| POST   | `/api/ephemeral-key` | `{ cardId }`                          | Short-lived key so the browser can reveal the full PAN/CVC via Stripe.js Issuing Elements |
| GET    | `/api/activity`   | `?cardId=&limit=`                       | Recent authorizations (approved **and** declined) |
| POST   | `/api/cancel-card`| `{ cardId }`                            | Cancel the card |
| POST   | `/api/webhook`    | raw Stripe event                        | Optional: log settled activity server-side |

All endpoints are plain `(req, res)` Node handlers in `api/`, usable either
as Vercel serverless functions or behind the included Express `server.js`.

## Run locally

```
cd server/stripe-issuing
cp .env.example .env   # fill in STRIPE_SECRET_KEY
npm install
npm start              # listens on :4242
```

## Deploy

**Vercel:** `vercel` from this directory, then set `STRIPE_SECRET_KEY`,
`STRIPE_WEBHOOK_SECRET`, and `ALLOWED_ORIGIN` (your deployed Evergrove URL)
as project environment variables.

**Render / Railway / Fly.io:** deploy as a normal Node service (`npm start`),
same environment variables.

Point Evergrove at whichever URL you deploy to from
Settings → Stripe Card → Backend URL in the app.

## Why no manual authorization webhook?

Cards created here rely on Stripe's built-in `spending_controls` to approve
or decline in real time — Stripe enforces the cap itself, synchronously, at
the point of sale. You only need to respond to `issuing_authorization.request`
within Stripe's webhook if you want *custom* approval logic beyond a simple
cap (e.g. merchant-category rules decided by your own server). The included
`/api/webhook` handler just logs settled activity; wire in your own
notification/sync logic where marked if you want it.
