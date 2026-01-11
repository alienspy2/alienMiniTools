// 메인 앱 로직

let currentCatalogId = null;
let currentAssets = [];
let viewer3d = null;
let selectedAssetId = null;

// DOM 요소
const themeForm = document.getElementById('themeForm');
const themeInput = document.getElementById('themeInput');
const generateBtn = document.getElementById('generateBtn');
const catalogSection = document.getElementById('catalogSection');
const catalogSelect = document.getElementById('catalogSelect');
const loadCatalogBtn = document.getElementById('loadCatalogBtn');
const addAssetsBtn = document.getElementById('addAssetsBtn');
const assetSection = document.getElementById('assetSection');
const assetList = document.getElementById('assetList');
const generateAllBtn = document.getElementById('generateAllBtn');
const exportBtn = document.getElementById('exportBtn');
const progressSection = document.getElementById('progressSection');
const progressFill = document.getElementById('progressFill');
const progressText = document.getElementById('progressText');
const viewerSection = document.getElementById('viewerSection');
const editModal = document.getElementById('editModal');
const editForm = document.getElementById('editForm');

// 초기화
document.addEventListener('DOMContentLoaded', async () => {
    // 3D 뷰어 초기화
    viewer3d = new Viewer3D('viewer3d');

    // 카탈로그 목록 로드
    await loadCatalogs();

    // 이벤트 리스너
    setupEventListeners();
});

function setupEventListeners() {
    // 테마 생성
    themeForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const theme = themeInput.value.trim();
        if (!theme) return;

        generateBtn.disabled = true;
        generateBtn.innerHTML = '<span class="loading"></span> 생성 중...';

        try {
            const result = await api.generateTheme(theme);
            currentCatalogId = result.catalog_id;
            await loadCatalog(currentCatalogId);
            await loadCatalogs();
            themeInput.value = '';
        } catch (error) {
            alert('에셋 리스트 생성 실패: ' + error.message);
        } finally {
            generateBtn.disabled = false;
            generateBtn.textContent = '에셋 생성';
        }
    });

    // 카탈로그 불러오기
    loadCatalogBtn.addEventListener('click', async () => {
        const catalogId = catalogSelect.value;
        if (catalogId) {
            await loadCatalog(catalogId);
        }
    });

    // 10개 에셋 추가
    addAssetsBtn.addEventListener('click', async () => {
        const catalogId = catalogSelect.value || currentCatalogId;
        if (!catalogId) {
            alert('먼저 카탈로그를 선택해주세요.');
            return;
        }

        addAssetsBtn.disabled = true;
        addAssetsBtn.innerHTML = '<span class="loading"></span> 추가 중...';

        try {
            const result = await api.addAssets(catalogId, 10);
            alert(`${result.added_count}개 에셋이 추가되었습니다.`);
            await loadCatalog(catalogId);
            await loadCatalogs();
        } catch (error) {
            alert('에셋 추가 실패: ' + error.message);
        } finally {
            addAssetsBtn.disabled = false;
            addAssetsBtn.textContent = '10개 추가';
        }
    });

    // 전체 생성
    generateAllBtn.addEventListener('click', async () => {
        if (!currentCatalogId) return;

        generateAllBtn.disabled = true;
        progressSection.style.display = 'block';
        progressFill.style.width = '0%';
        progressText.textContent = '배치 생성 시작 중...';

        let eventSource = null;

        try {
            // 먼저 SSE 연결 (상태 모니터링 준비)
            eventSource = api.streamGenerationStatus(currentCatalogId, (data) => {
                console.log('SSE data:', data);

                if (data.done) {
                    progressText.textContent = '완료!';
                    progressFill.style.width = '100%';
                    loadCatalog(currentCatalogId);
                    setTimeout(() => {
                        progressSection.style.display = 'none';
                        generateAllBtn.disabled = false;
                    }, 2000);
                } else if (data.waiting) {
                    progressText.textContent = '서버 응답 대기 중...';
                } else {
                    const percent = data.total > 0 ? Math.round((data.completed + data.failed) / data.total * 100) : 0;
                    progressFill.style.width = `${percent}%`;

                    let statusText = '';
                    if (data.current_status === 'starting') {
                        statusText = `준비 중... (${data.total}개 에셋)`;
                    } else if (data.current_status === 'generating') {
                        statusText = `${data.current_asset || '생성 중'} (${data.completed + data.failed + 1}/${data.total})`;
                        if (data.failed > 0) statusText += ` - 실패: ${data.failed}`;
                    } else if (data.current_status === 'completed') {
                        statusText = `완료! 성공: ${data.completed}, 실패: ${data.failed}`;
                    } else {
                        statusText = `${data.current_asset || ''} - ${data.completed}/${data.total}`;
                    }
                    progressText.textContent = statusText;

                    // 진행 중 에셋 목록 업데이트 (5초마다)
                    if ((data.completed + data.failed) % 1 === 0) {
                        loadCatalog(currentCatalogId);
                    }
                }
            });

            // 잠시 대기 후 배치 생성 시작 (SSE 연결 안정화)
            await new Promise(resolve => setTimeout(resolve, 200));

            // 배치 생성 API 호출 (백그라운드로 실행됨)
            const result = await api.generateAllAssets(currentCatalogId);
            console.log('Batch generation started:', result);

        } catch (error) {
            console.error('Batch generation error:', error);
            alert('배치 생성 시작 실패: ' + error.message);
            generateAllBtn.disabled = false;
            progressSection.style.display = 'none';
            if (eventSource) eventSource.close();
        }
    });

    // 내보내기
    exportBtn.addEventListener('click', () => {
        if (!currentCatalogId) return;
        window.location.href = api.getExportUrl(currentCatalogId);
    });

    // 편집 폼
    editForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const assetId = document.getElementById('editAssetId').value;

        try {
            await api.updateAsset(assetId, {
                name: document.getElementById('editName').value,
                name_kr: document.getElementById('editNameKr').value,
                category: document.getElementById('editCategory').value,
                description: document.getElementById('editDescription').value,
                prompt_2d: document.getElementById('editPrompt2d').value,
            });

            editModal.style.display = 'none';
            await loadCatalog(currentCatalogId);
        } catch (error) {
            alert('에셋 수정 실패: ' + error.message);
        }
    });

    document.getElementById('cancelEdit').addEventListener('click', () => {
        editModal.style.display = 'none';
    });

    // 다운로드 버튼
    document.getElementById('downloadGlb').addEventListener('click', () => {
        if (selectedAssetId) {
            window.location.href = api.getModelUrl(selectedAssetId, 'glb');
        }
    });

    document.getElementById('downloadObj').addEventListener('click', () => {
        if (selectedAssetId) {
            window.location.href = api.getModelUrl(selectedAssetId, 'obj');
        }
    });
}

async function loadCatalogs() {
    try {
        const result = await api.getCatalogs();
        catalogSelect.innerHTML = '<option value="">카탈로그 선택...</option>';

        for (const catalog of result.catalogs) {
            const option = document.createElement('option');
            option.value = catalog.id;
            option.textContent = `${catalog.name} (${catalog.completed_count}/${catalog.asset_count})`;
            catalogSelect.appendChild(option);
        }

        catalogSection.style.display = result.catalogs.length > 0 ? 'flex' : 'none';
    } catch (error) {
        console.error('카탈로그 로드 실패:', error);
    }
}

async function loadCatalog(catalogId) {
    try {
        const catalog = await api.getCatalog(catalogId);
        currentCatalogId = catalogId;
        currentAssets = catalog.assets;

        renderAssetList();
        assetSection.style.display = 'block';
    } catch (error) {
        alert('카탈로그 로드 실패: ' + error.message);
    }
}

function renderAssetList() {
    assetList.innerHTML = '';

    for (const asset of currentAssets) {
        const item = document.createElement('div');
        item.className = 'asset-item';

        const statusText = {
            pending: '대기',
            generating_2d: '2D 생성 중',
            generating_3d: '3D 생성 중',
            completed: '완료',
            failed: '실패',
        };

        const categoryText = {
            prop: '소품',
            furniture: '가구',
            wall: '벽',
            ceiling: '천장',
            floor: '바닥',
            decoration: '장식',
            lighting: '조명',
            other: '기타',
        };

        item.innerHTML = `
            <div class="asset-preview">
                ${asset.preview_url
                    ? `<img src="${asset.preview_url}" alt="${asset.name}">`
                    : '<span class="placeholder">미리보기<br>없음</span>'}
            </div>
            <div class="asset-info">
                <h3>${asset.name_kr || asset.name}</h3>
                <span class="category">${categoryText[asset.category] || asset.category}</span>
                <span class="status ${asset.status}">${statusText[asset.status] || asset.status}</span>
                ${asset.error_message ? `<div style="color:var(--error);font-size:0.8em;">${asset.error_message}</div>` : ''}
            </div>
            <div class="asset-actions">
                <button class="btn-edit" onclick="editAsset('${asset.id}')">편집</button>
                <button class="btn-generate" onclick="generateAsset('${asset.id}')" ${asset.status === 'generating_2d' || asset.status === 'generating_3d' ? 'disabled' : ''}>생성</button>
                ${asset.status === 'completed' ? `<button class="btn-view" onclick="viewAsset('${asset.id}')">3D 보기</button>` : ''}
                <button class="btn-delete" onclick="deleteAsset('${asset.id}')">삭제</button>
            </div>
        `;

        assetList.appendChild(item);
    }
}

async function editAsset(assetId) {
    const asset = currentAssets.find(a => a.id === assetId);
    if (!asset) return;

    document.getElementById('editAssetId').value = asset.id;
    document.getElementById('editName').value = asset.name;
    document.getElementById('editNameKr').value = asset.name_kr || '';
    document.getElementById('editCategory').value = asset.category;
    document.getElementById('editDescription').value = asset.description || '';
    document.getElementById('editPrompt2d').value = asset.prompt_2d || '';

    editModal.style.display = 'flex';
}

async function generateAsset(assetId) {
    try {
        const btn = event.target;
        btn.disabled = true;
        btn.textContent = '생성 중...';

        await api.generateAsset(assetId);
        await loadCatalog(currentCatalogId);
    } catch (error) {
        alert('에셋 생성 실패: ' + error.message);
        await loadCatalog(currentCatalogId);
    }
}

async function viewAsset(assetId) {
    const asset = currentAssets.find(a => a.id === assetId);
    if (!asset || !asset.model_glb_url) return;

    selectedAssetId = assetId;
    viewerSection.style.display = 'block';

    try {
        await viewer3d.loadModel(asset.model_glb_url);
        viewerSection.scrollIntoView({ behavior: 'smooth' });
    } catch (error) {
        alert('3D 모델 로드 실패: ' + error.message);
    }
}

async function deleteAsset(assetId) {
    if (!confirm('이 에셋을 삭제하시겠습니까?')) return;

    try {
        await api.deleteAsset(assetId);
        await loadCatalog(currentCatalogId);
    } catch (error) {
        alert('에셋 삭제 실패: ' + error.message);
    }
}
