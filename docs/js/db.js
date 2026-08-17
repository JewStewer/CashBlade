// IndexedDB helper — called via JS interop from Blazor
const DB_NAME = 'FinanceBlade';
const STORES = [
    'accounts', 'categories', 'transactions', 'bills',
    'billOccurrenceStatuses', 'debts', 'debtPayments',
    'savingsGoals', 'weeklyBudgets', 'appSettings', 'syncMeta', 'trips'
];
// Stores that survive clearAll (phone-side overrides, lent money tracking)
const PERSISTENT_STORES = ['pendingBillOverrides', 'billDeletes', 'debtDeletes', 'savingsGoalDeletes', 'lentTxns', 'transactionOverrides', 'transactionDeletes', 'tripDeletes', 'tripOverrides', 'settingOverrides', 'billEditOverrides', 'debtOverrides', 'accountOverrides', 'savingsGoalOverrides'];
const ALL_STORES = [...STORES, ...PERSISTENT_STORES];

let _db = null;

function bindVersionChange(db) {
    db.onversionchange = () => {
        db.close();
        if (_db === db) _db = null;
    };
    return db;
}

function existingStores(db, stores) {
    return stores.filter(store => db.objectStoreNames.contains(store));
}

function openDb() {
    if (_db) return Promise.resolve(_db);
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME);
        req.onupgradeneeded = e => {
            const db = e.target.result;
            for (const store of ALL_STORES) {
                if (!db.objectStoreNames.contains(store)) {
                    db.createObjectStore(store, { keyPath: 'id' });
                }
            }
        };
        req.onsuccess = e => { _db = bindVersionChange(e.target.result); resolve(_db); };
        req.onerror = e => reject(e.target.error);
    });
}

async function ensureStore(store) {
    let db = await openDb();
    if (db.objectStoreNames.contains(store)) return db;

    const nextVersion = db.version + 1;
    db.close();
    _db = null;
    const storesToEnsure = ALL_STORES.includes(store)
        ? ALL_STORES
        : [...ALL_STORES, store];

    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, nextVersion);
        req.onupgradeneeded = e => {
            const upgraded = e.target.result;
            for (const name of storesToEnsure) {
                if (!upgraded.objectStoreNames.contains(name)) {
                    upgraded.createObjectStore(name, { keyPath: 'id' });
                }
            }
        };
        req.onsuccess = e => {
            _db = bindVersionChange(e.target.result);
            if (!_db.objectStoreNames.contains(store)) {
                reject(new Error(`IndexedDB store "${store}" is missing after schema upgrade.`));
                return;
            }
            resolve(_db);
        };
        req.onerror = e => reject(e.target.error);

        // Another open connection (e.g. a backgrounded/suspended tab) hasn't
        // closed yet. That connection's own onversionchange handler may still
        // close it shortly — onblocked isn't final, so give it a grace window
        // before giving up instead of failing immediately.
        let blocked = false;
        req.onblocked = () => {
            blocked = true;
            setTimeout(() => {
                if (blocked) reject(new Error('Database upgrade blocked. Close other Evergrove tabs and reopen the app.'));
            }, 4000);
        };
    });
}

window.db = {
    async getAll(store) {
        const db = await ensureStore(store);
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readonly');
            const req = tx.objectStore(store).getAll();
            req.onsuccess = () => resolve(req.result);
            req.onerror = e => reject(e.target.error);
        });
    },

    async get(store, id) {
        const db = await ensureStore(store);
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readonly');
            const req = tx.objectStore(store).get(id);
            req.onsuccess = () => resolve(req.result ?? null);
            req.onerror = e => reject(e.target.error);
        });
    },

    async put(store, record) {
        const db = await ensureStore(store);
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const req = tx.objectStore(store).put(record);
            req.onsuccess = () => resolve();
            req.onerror = e => reject(e.target.error);
        });
    },

    async putBulk(store, records) {
        const db = await ensureStore(store);
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const os = tx.objectStore(store);
            for (const r of records) os.put(r);
            tx.oncomplete = () => resolve();
            tx.onerror = e => reject(e.target.error);
        });
    },

    async delete(store, id) {
        const db = await ensureStore(store);
        return new Promise((resolve, reject) => {
            const tx = db.transaction(store, 'readwrite');
            const req = tx.objectStore(store).delete(id);
            req.onsuccess = () => resolve();
            req.onerror = e => reject(e.target.error);
        });
    },

    // Atomically deletes a debt, writes its tombstone, and clears its override
    // in a single IndexedDB transaction. If the process is killed mid-way,
    // IndexedDB rolls back the whole transaction — no partial state is possible.
    async deleteDebt(id, tombstone) {
        const db = await openDb();
        const needed = ['debts', 'debtOverrides'];
        if (tombstone !== null && tombstone !== undefined) needed.push('debtDeletes');
        const stores = existingStores(db, needed);
        return new Promise((resolve, reject) => {
            const tx = db.transaction(stores, 'readwrite');
            tx.oncomplete = () => resolve();
            tx.onerror = e => reject(e.target.error ?? new Error('deleteDebt failed'));
            tx.onabort  = () => reject(tx.error  ?? new Error('deleteDebt aborted'));
            if (stores.includes('debts'))
                tx.objectStore('debts').delete(id);
            if (stores.includes('debtOverrides'))
                tx.objectStore('debtOverrides').delete(id);
            if (tombstone !== null && tombstone !== undefined && stores.includes('debtDeletes'))
                tx.objectStore('debtDeletes').put(tombstone);
        });
    },

    async clear(store) {
        const db = await ensureStore(store);
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
        const stores = existingStores(db, STORES);
        if (stores.length === 0) return;
        return new Promise((resolve, reject) => {
            const tx = db.transaction(stores, 'readwrite');
            for (const s of stores) tx.objectStore(s).clear();
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
        const stores = existingStores(db, STORES);
        if (stores.length === 0) return;
        return new Promise((resolve, reject) => {
            const tx = db.transaction(stores, 'readwrite');
            tx.oncomplete = () => resolve();
            tx.onerror  = e => reject(e.target.error ?? tx.error ?? new Error('replaceAll failed'));
            tx.onabort  = () => reject(tx.error ?? new Error('replaceAll aborted'));

            // Clear every sync-managed store first (all inside the same transaction)
            for (const s of stores) tx.objectStore(s).clear();

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
                trips:                  data.trips                  ?? [],
            };
            for (const [store, records] of Object.entries(storeMap)) {
                if (!db.objectStoreNames.contains(store)) continue;
                const os = tx.objectStore(store);
                for (const r of records) os.put(r);
            }

            // Restore sync meta (credentials + last-synced timestamp)
            if (data.syncMeta && db.objectStoreNames.contains('syncMeta')) {
                tx.objectStore('syncMeta').put(data.syncMeta);
            }
        });
    }
};
