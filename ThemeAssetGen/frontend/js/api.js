// API Module

const API_BASE = '/api';

const api = {
    // Theme related (streaming with progress)
    streamGenerateTheme(theme, onProgress) {
        return new Promise((resolve, reject) => {
            fetch(`${API_BASE}/theme/generate-stream`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ theme }),
            }).then(response => {
                if (!response.ok) {
                    reject(new Error('Theme generation failed'));
                    return;
                }

                const reader = response.body.getReader();
                const decoder = new TextDecoder();
                let buffer = '';

                function read() {
                    reader.read().then(({ done, value }) => {
                        if (done) {
                            resolve();
                            return;
                        }

                        buffer += decoder.decode(value, { stream: true });
                        const lines = buffer.split('\n');
                        buffer = lines.pop(); // Keep incomplete line in buffer

                        for (const line of lines) {
                            if (line.startsWith('data: ')) {
                                try {
                                    const data = JSON.parse(line.slice(6));
                                    onProgress(data);

                                    if (data.stage === 'done') {
                                        resolve(data);
                                    } else if (data.stage === 'error') {
                                        reject(new Error(data.message));
                                    }
                                } catch (e) {
                                    console.error('SSE parse error:', e);
                                }
                            }
                        }

                        read();
                    }).catch(reject);
                }

                read();
            }).catch(reject);
        });
    },

    // Theme related (non-streaming fallback)
    async generateTheme(theme) {
        const response = await fetch(`${API_BASE}/theme/generate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ theme }),
        });
        if (!response.ok) throw new Error('Theme generation failed');
        return response.json();
    },

    // Asset related
    async getAsset(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}`);
        if (!response.ok) throw new Error('Asset fetch failed');
        return response.json();
    },

    async updateAsset(assetId, data) {
        const response = await fetch(`${API_BASE}/asset/${assetId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data),
        });
        if (!response.ok) throw new Error('Asset update failed');
        return response.json();
    },

    async deleteAsset(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}`, {
            method: 'DELETE',
        });
        if (!response.ok) throw new Error('Asset delete failed');
        return response.json();
    },

    async generateAsset(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}/generate`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('Asset generation failed');
        return response.json();
    },

    async generateAsset2D(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}/generate-2d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('2D generation failed');
        return response.json();
    },

    async generateAsset3D(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}/generate-3d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('3D generation failed');
        return response.json();
    },

    // Catalog related
    async getCatalogs() {
        const response = await fetch(`${API_BASE}/catalog/list`);
        if (!response.ok) throw new Error('Catalog list fetch failed');
        return response.json();
    },

    async getCatalog(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}`);
        if (!response.ok) throw new Error('Catalog fetch failed');
        return response.json();
    },

    async deleteCatalog(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}`, {
            method: 'DELETE',
        });
        if (!response.ok) throw new Error('Catalog delete failed');
        return response.json();
    },

    async generate2DAssets(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}/generate-2d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('2D batch start failed');
        return response.json();
    },

    async generate3DAssets(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}/generate-3d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('3D batch start failed');
        return response.json();
    },

    async generateAllAssets(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}/generate-all`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('Batch start failed');
        return response.json();
    },

    async addAssets(catalogId, assetType = null, count = null) {
        let url = `${API_BASE}/catalog/${catalogId}/add-assets`;
        const params = [];
        if (assetType) params.push(`asset_type=${assetType}`);
        if (count) params.push(`count=${count}`);
        if (params.length > 0) url += `?${params.join('&')}`;

        const response = await fetch(url, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('Asset addition failed');
        return response.json();
    },

    async getAssetCategories() {
        const response = await fetch(`${API_BASE}/catalog/asset-categories`);
        if (!response.ok) throw new Error('Category fetch failed');
        return response.json();
    },

    getExportUrl(catalogId) {
        return `${API_BASE}/catalog/${catalogId}/export`;
    },

    // Generation queue SSE stream
    streamQueueStatus(catalogId, onMessage) {
        const eventSource = new EventSource(`${API_BASE}/generation/stream/${catalogId}`);

        eventSource.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                onMessage(data);

                if (data.done) {
                    eventSource.close();
                }
            } catch (e) {
                console.error('SSE parse error:', e);
            }
        };

        eventSource.onerror = (err) => {
            console.error('SSE error:', err);
            eventSource.close();
        };

        return eventSource;
    },

    // Get queue status (REST)
    async getQueueStatus(catalogId) {
        const response = await fetch(`${API_BASE}/generation/status/${catalogId}`);
        if (!response.ok) throw new Error('Queue status fetch failed');
        return response.json();
    },

    // Clear queue
    async clearQueue(catalogId) {
        const response = await fetch(`${API_BASE}/generation/clear/${catalogId}`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('Queue clear failed');
        return response.json();
    },

    // File URLs
    getPreviewUrl(assetId) {
        return `/files/preview/${assetId}`;
    },

    getModelUrl(assetId, format) {
        return `/files/model/${assetId}/${format}`;
    },
};
