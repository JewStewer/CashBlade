const ALLOWED_ORIGIN = process.env.ALLOWED_ORIGIN || '*';

function withCors(handler) {
    return async (req, res) => {
        res.setHeader('Access-Control-Allow-Origin', ALLOWED_ORIGIN);
        res.setHeader('Access-Control-Allow-Methods', 'GET,POST,OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
        if (req.method === 'OPTIONS') {
            res.status(204).end();
            return;
        }
        return handler(req, res);
    };
}

module.exports = { withCors };
