window.imbalancesPersistence = {
    dbName: "ImbalancesDB",
    storeName: "StateStore",
    
    initDB: function () {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(this.dbName);

            request.onupgradeneeded = (event) => {
                const db = event.target.result;
                if (!db.objectStoreNames.contains(this.storeName)) {
                    db.createObjectStore(this.storeName);
                }
            };

            request.onsuccess = (event) => {
                const db = event.target.result;
                if (db.objectStoreNames.contains(this.storeName)) {
                    resolve(db);
                    return;
                }

                const nextVersion = db.version + 1;
                db.close();

                const upgradeRequest = indexedDB.open(this.dbName, nextVersion);
                upgradeRequest.onupgradeneeded = (upgradeEvent) => {
                    const upgradeDb = upgradeEvent.target.result;
                    if (!upgradeDb.objectStoreNames.contains(this.storeName)) {
                        upgradeDb.createObjectStore(this.storeName);
                    }
                };
                upgradeRequest.onsuccess = (upgradeEvent) => resolve(upgradeEvent.target.result);
                upgradeRequest.onerror = (upgradeEvent) => reject(upgradeEvent.target.error);
            };

            request.onerror = (event) => reject(event.target.error);
        });
    },

    saveItem: async function (key, value) {
        const db = await this.initDB();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction([this.storeName], "readwrite");
            const store = transaction.objectStore(this.storeName);
            const request = store.put(value, key);

            request.onsuccess = () => resolve(true);
            request.onerror = (e) => reject(e.target.error);
        });
    },

    loadItem: async function (key) {
        const db = await this.initDB();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction([this.storeName], "readonly");
            const store = transaction.objectStore(this.storeName);
            const request = store.get(key);

            request.onsuccess = (e) => resolve(e.target.result || null);
            request.onerror = (e) => reject(e.target.error);
        });
    },

    deleteItem: async function (key) {
        const db = await this.initDB();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction([this.storeName], "readwrite");
            const store = transaction.objectStore(this.storeName);
            const request = store.delete(key);

            request.onsuccess = () => resolve(true);
            request.onerror = (e) => reject(e.target.error);
        });
    },

    saveState: async function (stateObj) {
        return await this.saveItem("appState", stateObj);
    },

    loadState: async function () {
        return await this.loadItem("appState");
    },

    clearState: async function () {
        return await this.deleteItem("appState");
    }
};
