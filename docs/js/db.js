// IndexedDB helper — called via JS interop from Blazor
const DB_NAME = 'FinanceBlade';
const DB_VERSION = 3;
const STORES = [
    'accounts', 'categories', 'transactions', 'bills',
    'billOccurrenceStatuses', 'debts', 'debtPayments',
    'savingsGoals', 'weeklyBudgets', 'appSettings', 'syncMeta'
];
// Stores that survive clearAll (phone-side overrides, lent money tracking)
const PERSISTENT_STORES = ['pendingBillOverrides', 'lentTxns'];
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
            tx.onerror  = e => reject(e.target.error);
            tx.onabort  = () => reject(tx.error ?? new Error('clearAll aborted'));
        });
    },

    // Atomic sync replace — clears all sync-managed stores AND writes the new data
    // inside a SINGLE IndexedDB transaction.  If any write fails (e.g. Safari iOS
    // storage quota or a bad record) the entire transaction rolls back automatically,
    // so the existing data is preserved instead of being left in a half-wiped state.
    async replaceAll(data) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORES, 'readwrite');
            tx.oncomplete = () => resolve();
            tx.onerror  = e => reject(e.target.error ?? tx.error ?? new Error('replaceAll failed'));
            tx.onabort  = () => reject(tx.error ?? new Error('replaceAll aborted'));

            // Clear every sync-managed store first (all inside the same transaction)
            for (const s of STORES) tx.objectStore(s).clear();

            // Write incoming records into each store
            const storeMap = {
                accounts:               data.accounts               ?? [],
                categories:             data.categories             ?? [],
                transactions:           data.transactions           ?? [],
                bills:                  data.bills                  ?? [],
                billOccurrenceStatuses: data.billOccurrenceStatuses ?? [],
                debts:                  data.debts                  ?? [],
                debtPayments:           data.debtPayments           ?? [],
                savingsGoals:           data.savingsGoals           ?? [],
                weeklyBudgets:          data.weeklyBudgets          ?? [],
                appSettings:            data.appSettings            ?? [],
            };
            for (const [store, records] of Object.entries(storeMap)) {
                const os = tx.objectStore(store);
                for (const r of records) os.put(r);
            }

            // Restore sync meta (credentials + last-synced timestamp)
            if (data.syncMeta) tx.objectStore('syncMeta').put(data.syncMeta);
        });
    }
};
