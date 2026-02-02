// API 호출 모듈

const API_BASE = '/api';

const api = {
    // 테마 관련
    async generateTheme(theme) {
        const response = await fetch(`${API_BASE}/theme/generate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ theme }),
        });
        if (!response.ok) throw new Error('테마 생성 실패');
        return response.json();
    },

    // 에셋 관련
    async getAsset(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}`);
        if (!response.ok) throw new Error('에셋 조회 실패');
        return response.json();
    },

    async updateAsset(assetId, data) {
        const response = await fetch(`${API_BASE}/asset/${assetId}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data),
        });
        if (!response.ok) throw new Error('에셋 수정 실패');
        return response.json();
    },

    async deleteAsset(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}`, {
            method: 'DELETE',
        });
        if (!response.ok) throw new Error('에셋 삭제 실패');
        return response.json();
    },

    async generateAsset(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}/generate`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('에셋 생성 실패');
        return response.json();
    },

    async generateAsset2D(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}/generate-2d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('2D 생성 실패');
        return response.json();
    },

    async generateAsset3D(assetId) {
        const response = await fetch(`${API_BASE}/asset/${assetId}/generate-3d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('3D 생성 실패');
        return response.json();
    },

    // 카탈로그 관련
    async getCatalogs() {
        const response = await fetch(`${API_BASE}/catalog/list`);
        if (!response.ok) throw new Error('카탈로그 목록 조회 실패');
        return response.json();
    },

    async getCatalog(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}`);
        if (!response.ok) throw new Error('카탈로그 조회 실패');
        return response.json();
    },

    async deleteCatalog(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}`, {
            method: 'DELETE',
        });
        if (!response.ok) throw new Error('카탈로그 삭제 실패');
        return response.json();
    },

    async generate2DAssets(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}/generate-2d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('2D 배치 생성 시작 실패');
        return response.json();
    },

    async generate3DAssets(catalogId) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}/generate-3d`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('3D 배치 생성 시작 실패');
        return response.json();
    },

    async addAssets(catalogId, count = 10) {
        const response = await fetch(`${API_BASE}/catalog/${catalogId}/add-assets?count=${count}`, {
            method: 'POST',
        });
        if (!response.ok) throw new Error('에셋 추가 실패');
        return response.json();
    },

    getExportUrl(catalogId) {
        return `${API_BASE}/catalog/${catalogId}/export`;
    },

    // 생성 진행률 SSE
    streamGenerationStatus(catalogId, onMessage) {
        const eventSource = new EventSource(`${API_BASE}/generation/stream/${catalogId}`);

        eventSource.onmessage = (event) => {
            const data = JSON.parse(event.data);
            onMessage(data);

            if (data.done) {
                eventSource.close();
            }
        };

        eventSource.onerror = () => {
            eventSource.close();
        };

        return eventSource;
    },

    // 파일 URL
    getPreviewUrl(assetId) {
        return `/files/preview/${assetId}`;
    },

    getModelUrl(assetId, format) {
        return `/files/model/${assetId}/${format}`;
    },
};
