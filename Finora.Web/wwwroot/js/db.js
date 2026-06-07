// IndexedDB helper — called via JS interop from Blazor
const DB_NAME = 'FinanceBlade';
const DB_VERSION = 2;
const STORES = [
    'accounts', 'categories', 'transactions', 'bills',
    'billOccurrenceStatuses', 'debts', 'debtPayments',
    'savingsGoals', 'weeklyBudgets', 'appSettings', 'syncMeta'
];
// Stores that survive clearAll (phone-side overrides)
const PERSISTENT_STORES = ['pendingBillOverrides'];
const ALL_STORES = [...STORES, ...PERSISTENT_STORES];

let _db = null;

function openDb() {
    if (_db) return Promise.resolve(_db);
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = e => {
            const db = e.target.result;
            for (const store of ALL_STORES) {
                if (!db.objectStoreNames.contains(store)) {
                    db.createObjectStore(store, { keyPath: 'id' });
                }
            }
        };
        req.onsuccess = e => { _db = e.target.result; resolve(_db); };
        req.onerror = e => reject(e.target.error);
    });
}

window.db = {
    async getAll(store) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readonly');
            const req = tx.objectStore(store).getAll();
            req.onsuccess = () => resolve(req.result);
            req.onerror = e => reject(e.target.error);
        });
    },

    async get(store, id) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readonly');
            const req = tx.objectStore(store).get(id);
            req.onsuccess = () => resolve(req.result ?? null);
            req.onerror = e => reject(e.target.error);
        });
    },

    async put(store, record) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const req = tx.objectStore(store).put(record);
            req.onsuccess = () => resolve();
            req.onerror = e => reject(e.target.error);
        });
    },

    async putBulk(store, records) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const os = tx.objectStore(store);
            for (const r of records) os.put(r);
            tx.oncomplete = () => resolve();
            tx.onerror = e => reject(e.target.error);
        });
    },

    async delete(store, id) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const req = tx.objectStore(store).delete(id);
            req.onsuccess = () => resolve();
            req.onerror = e => reject(e.target.error);
        });
    },

    async clear(store) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const req = tx.objectStore(store).clear();
            req.onsuccess = () => resolve();
            req.onerror = e => reject(e.target.error);
        });
    },

    // clearAll only clears sync-managed stores; PERSISTENT_STORES survive
    async clearAll() {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORES, 'readwrite');
            for (const s of STORES) tx.objectStore(s).clear();
            tx.oncomplete = () => resolve();
            tx.onerror = e => reject(e.target.error);
        });
    }
};
