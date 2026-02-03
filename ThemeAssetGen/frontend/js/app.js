// Main App Logic

let currentCatalogId = null;
let currentAssets = [];
let viewer3d = null;
let selectedAssetId = null;
let eventSource = null;

// DOM Elements
const themeForm = document.getElementById('themeForm');
const themeInput = document.getElementById('themeInput');
const generateBtn = document.getElementById('generateBtn');
const catalogSection = document.getElementById('catalogSection');
const catalogSelect = document.getElementById('catalogSelect');
const loadCatalogBtn = document.getElementById('loadCatalogBtn');
const addAssetsSection = document.getElementById('addAssetsSection');
const categoryButtonsContainer = document.getElementById('categoryButtons');
const assetSection = document.getElementById('assetSection');
const assetList = document.getElementById('assetList');
const generateAllBtn = document.getElementById('generateAllBtn');
const generate2DBtn = document.getElementById('generate2DBtn');
const generate3DBtn = document.getElementById('generate3DBtn');
const exportBtn = document.getElementById('exportBtn');
const queuePanel = document.getElementById('queuePanel');
const viewerPanel = document.getElementById('viewerPanel');
const viewerHeader = document.getElementById('viewerHeader');
const viewerContent = document.getElementById('viewerContent');
const toggleViewerBtn = document.getElementById('toggleViewer');
const editModal = document.getElementById('editModal');
const editForm = document.getElementById('editForm');

// Queue DOM
const queue2DSection = document.getElementById('queue2DSection');
const queue2DStatus = document.getElementById('queue2DStatus');
const queue2DCurrent = document.getElementById('queue2DCurrent');
const queue2DProgress = document.getElementById('queue2DProgress');
const queue2DPending = document.getElementById('queue2DPending');
const queue3DSection = document.getElementById('queue3DSection');
const queue3DStatus = document.getElementById('queue3DStatus');
const queue3DCurrent = document.getElementById('queue3DCurrent');
const queue3DProgress = document.getElementById('queue3DProgress');
const queue3DPending = document.getElementById('queue3DPending');

// Initialize
document.addEventListener('DOMContentLoaded', async () => {
    viewer3d = new Viewer3D('viewer3d');
    await loadCatalogs();
    await loadAssetCategories();
    setupEventListeners();
});

function setupEventListeners() {
    // Theme generation with progress
    themeForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        const theme = themeInput.value.trim();
        if (!theme) return;

        generateBtn.disabled = true;
        generateBtn.innerHTML = '<span class="loading"></span> Generating...';

        // Show progress panel
        const progressPanel = document.getElementById('themeProgress');
        const progressText = document.getElementById('themeProgressText');
        const progressBar = document.getElementById('themeProgressBar');
        const progressDetail = document.getElementById('themeProgressDetail');

        progressPanel.style.display = 'block';
        progressBar.style.width = '0%';
        progressText.textContent = 'Starting...';
        progressDetail.textContent = '';

        // Track elapsed time - update progressText instead of progressDetail
        let startTime = Date.now();
        let currentCategory = 'Wall Texture';
        let categoryIndex = 1;
        let totalCategories = 7;
        const elapsedTimer = setInterval(() => {
            const elapsed = Math.floor((Date.now() - startTime) / 1000);
            progressText.textContent = `Generating ${currentCategory} (${categoryIndex}/${totalCategories})... ${elapsed}s`;
        }, 1000);

        try {
            let lastCategoryIndex = 0;
            await api.streamGenerateTheme(theme, (data) => {
                console.log('Theme progress:', data);

                if (data.stage === 'generating') {
                    // Only reset timer when category actually changes
                    if (data.category_index !== lastCategoryIndex) {
                        startTime = Date.now();
                        lastCategoryIndex = data.category_index;
                    }
                    currentCategory = data.category_name;
                    categoryIndex = data.category_index;
                    totalCategories = data.total_categories;
                    const pct = Math.round((data.category_index / data.total_categories) * 100);
                    progressBar.style.width = `${pct}%`;
                    progressDetail.textContent = `✅ Categories done: ${data.category_index - 1}/${data.total_categories}`;
                } else if (data.stage === 'completed') {
                    progressBar.style.width = '100%';
                    progressText.textContent = data.message;
                    progressDetail.textContent = '✅ All categories generated!';
                } else if (data.stage === 'done') {
                    currentCatalogId = data.catalog_id;
                    progressText.textContent = `Done! Created ${data.asset_count} assets`;
                }
            });

            clearInterval(elapsedTimer);

            await loadCatalog(currentCatalogId);
            await loadCatalogs();
            themeInput.value = '';

            // Hide progress after delay
            setTimeout(() => {
                progressPanel.style.display = 'none';
            }, 2000);

        } catch (error) {
            clearInterval(elapsedTimer);
            alert('Asset list generation failed: ' + error.message);
            progressPanel.style.display = 'none';
        } finally {
            generateBtn.disabled = false;
            generateBtn.textContent = 'Generate Assets';
        }
    });

    // Load catalog
    loadCatalogBtn.addEventListener('click', async () => {
        const catalogId = catalogSelect.value;
        if (catalogId) {
            await loadCatalog(catalogId);
        }
    });

    // Delete catalog
    document.getElementById('deleteCatalogBtn').addEventListener('click', async () => {
        const catalogId = catalogSelect.value;
        if (!catalogId) {
            alert('Please select a catalog first.');
            return;
        }

        const selectedOption = catalogSelect.options[catalogSelect.selectedIndex];
        if (!confirm(`Delete "${selectedOption.textContent}"?`)) {
            return;
        }

        try {
            await api.deleteCatalog(catalogId);
            await loadCatalogs();

            // Reset view
            currentCatalogId = null;
            currentAssets = [];
            assetSection.style.display = 'none';
            addAssetsSection.style.display = 'none';

            alert('Catalog deleted.');
        } catch (error) {
            alert('Delete failed: ' + error.message);
        }
    });

    // Category add buttons are set up dynamically in loadAssetCategories()

    // Generate All (2D + 3D parallel)
    generateAllBtn.addEventListener('click', async () => {
        if (!currentCatalogId) return;
        await startGeneration('all');
    });

    // 2D Only
    generate2DBtn.addEventListener('click', async () => {
        if (!currentCatalogId) return;
        await startGeneration('2d');
    });

    // 3D Only
    generate3DBtn.addEventListener('click', async () => {
        if (!currentCatalogId) return;
        await startGeneration('3d');
    });

    // Export
    exportBtn.addEventListener('click', () => {
        if (!currentCatalogId) return;
        window.location.href = api.getExportUrl(currentCatalogId);
    });

    // Edit form
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
            alert('Asset update failed: ' + error.message);
        }
    });

    document.getElementById('cancelEdit').addEventListener('click', () => {
        editModal.style.display = 'none';
    });

    // Download buttons
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

    // 3D viewer toggle
    toggleViewerBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        toggleViewerPanel();
    });

    viewerHeader.addEventListener('click', () => {
        toggleViewerPanel();
    });
}

function toggleViewerPanel() {
    const isCollapsed = viewerPanel.classList.toggle('collapsed');
    toggleViewerBtn.innerHTML = isCollapsed ? '&#9650;' : '&#9660;';

    if (!isCollapsed && viewer3d && viewer3d.initialized) {
        setTimeout(() => viewer3d.onResize(), 100);
    }
}

async function loadCatalogs() {
    try {
        const result = await api.getCatalogs();
        catalogSelect.innerHTML = '<option value="">Select catalog...</option>';

        for (const catalog of result.catalogs) {
            const option = document.createElement('option');
            option.value = catalog.id;
            option.textContent = `${catalog.name} (${catalog.completed_count}/${catalog.asset_count})`;
            catalogSelect.appendChild(option);
        }

        catalogSection.style.display = result.catalogs.length > 0 ? 'flex' : 'none';
    } catch (error) {
        console.error('Catalog load failed:', error);
    }
}

async function loadCatalog(catalogId) {
    try {
        const catalog = await api.getCatalog(catalogId);
        currentCatalogId = catalogId;
        currentAssets = catalog.assets;

        renderAssetList();
        assetSection.style.display = 'block';
        addAssetsSection.style.display = 'block';
    } catch (error) {
        alert('Catalog load failed: ' + error.message);
    }
}

// Asset category definitions
let assetCategories = {};

async function loadAssetCategories() {
    try {
        const result = await api.getAssetCategories();
        assetCategories = result.categories;
        renderCategoryButtons();
    } catch (error) {
        console.error('Category load failed:', error);
        // Fallback categories
        assetCategories = {
            wall_texture: { name: 'Wall Texture', count: 10, category: 'wall' },
            stair: { name: 'Stair', count: 3, category: 'floor' },
            floor_texture: { name: 'Floor Texture', count: 10, category: 'floor' },
            door: { name: 'Door', count: 5, category: 'wall' },
            prop_small: { name: 'Small Prop', count: 10, category: 'prop' },
            prop_medium: { name: 'Medium Prop', count: 10, category: 'prop' },
            prop_large: { name: 'Large Prop', count: 10, category: 'furniture' },
        };
        renderCategoryButtons();
    }
}

function renderCategoryButtons() {
    categoryButtonsContainer.innerHTML = '';

    // Group: Background
    const bgGroup = document.createElement('div');
    bgGroup.className = 'button-group';
    bgGroup.innerHTML = '<span class="group-label">Background:</span>';

    // Group: Props
    const propGroup = document.createElement('div');
    propGroup.className = 'button-group';
    propGroup.innerHTML = '<span class="group-label">Props:</span>';

    const bgTypes = ['wall_texture', 'stair', 'floor_texture', 'door'];
    const propTypes = ['prop_small', 'prop_medium', 'prop_large'];

    for (const [assetType, info] of Object.entries(assetCategories)) {
        const btn = document.createElement('button');
        btn.className = 'btn-category';
        btn.dataset.assetType = assetType;
        btn.innerHTML = `+${info.count} ${info.name}`;
        btn.addEventListener('click', () => addAssetsByCategory(assetType, btn));

        if (bgTypes.includes(assetType)) {
            bgGroup.appendChild(btn);
        } else if (propTypes.includes(assetType)) {
            propGroup.appendChild(btn);
        }
    }

    categoryButtonsContainer.appendChild(bgGroup);
    categoryButtonsContainer.appendChild(propGroup);
}

async function addAssetsByCategory(assetType, btn) {
    const catalogId = catalogSelect.value || currentCatalogId;
    if (!catalogId) {
        alert('Please select a catalog first.');
        return;
    }

    const originalText = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<span class="loading"></span> Adding...';

    try {
        const result = await api.addAssets(catalogId, assetType);
        alert(`${result.added_count} ${assetType} assets added.`);
        await loadCatalog(catalogId);
        await loadCatalogs();
    } catch (error) {
        alert('Asset addition failed: ' + error.message);
    } finally {
        btn.disabled = false;
        btn.innerHTML = originalText;
    }
}

function renderAssetList() {
    assetList.innerHTML = '';

    const statusText = {
        pending: 'Pending',
        generating_2d: '2D Generating',
        generating_3d: '2D Done (3D Pending)',
        completed: 'Completed',
        failed: 'Failed',
    };

    const categoryText = {
        prop: 'Prop',
        furniture: 'Furniture',
        wall: 'Wall',
        ceiling: 'Ceiling',
        floor: 'Floor',
        decoration: 'Decoration',
        lighting: 'Lighting',
        other: 'Other',
    };

    for (const asset of currentAssets) {
        const item = document.createElement('div');
        item.className = 'asset-item';

        item.innerHTML = `
            <div class="asset-preview">
                ${asset.preview_url
                ? `<img src="${asset.preview_url}" alt="${asset.name}">`
                : '<span class="placeholder">No<br>Preview</span>'}
            </div>
            <div class="asset-info">
                <h3>${asset.name_kr || asset.name}</h3>
                <span class="category">${categoryText[asset.category] || asset.category}</span>
                <span class="status ${asset.status}">${statusText[asset.status] || asset.status}</span>
                ${asset.error_message ? `<div style="color:var(--error);font-size:0.8em;">${asset.error_message}</div>` : ''}
            </div>
            <div class="asset-actions">
                <button class="btn-edit" onclick="editAsset('${asset.id}')">Edit</button>
                <button class="btn-2d" onclick="generate2DAsset('${asset.id}')" ${asset.status === 'generating_2d' ? 'disabled' : ''}>2D</button>
                <button class="btn-3d" onclick="generate3DAsset('${asset.id}')">3D</button>
                ${asset.status === 'completed' ? `<button class="btn-view" onclick="viewAsset('${asset.id}')">View 3D</button>` : ''}
                <button class="btn-delete" onclick="deleteAsset('${asset.id}')">Delete</button>
            </div>
        `;

        assetList.appendChild(item);
    }
}

// === Generation with Queue UI ===

async function startGeneration(type) {
    // Disable all buttons
    generateAllBtn.disabled = true;
    generate2DBtn.disabled = true;
    generate3DBtn.disabled = true;

    // Show queue panel
    queuePanel.style.display = 'block';
    resetQueueUI();

    // Close existing SSE
    if (eventSource) {
        eventSource.close();
        eventSource = null;
    }

    try {
        // Start SSE stream first
        eventSource = api.streamQueueStatus(currentCatalogId, handleQueueUpdate);

        // Wait for SSE to connect
        await new Promise(resolve => setTimeout(resolve, 200));

        // Start generation
        if (type === 'all') {
            await api.generateAllAssets(currentCatalogId);
        } else if (type === '2d') {
            await api.generate2DAssets(currentCatalogId);
        } else if (type === '3d') {
            await api.generate3DAssets(currentCatalogId);
        }

    } catch (error) {
        console.error('Generation start error:', error);
        alert('Generation failed: ' + error.message);
        resetButtons();
        queuePanel.style.display = 'none';
        if (eventSource) {
            eventSource.close();
            eventSource = null;
        }
    }
}

function handleQueueUpdate(data) {
    console.log('Queue update:', data);

    if (data.done) {
        // Generation complete
        queue2DStatus.textContent = 'Complete';
        queue2DStatus.className = 'queue-status complete';
        queue3DStatus.textContent = 'Complete';
        queue3DStatus.className = 'queue-status complete';

        // Refresh catalog
        loadCatalog(currentCatalogId);

        setTimeout(() => {
            resetButtons();
            // Keep queue panel visible for a moment to show results
            setTimeout(() => {
                queuePanel.style.display = 'none';
            }, 3000);
        }, 1000);

        return;
    }

    if (data.waiting) {
        queue2DStatus.textContent = 'Waiting...';
        queue3DStatus.textContent = 'Waiting...';
        return;
    }

    // Update 2D Queue UI
    update2DQueueUI(data);

    // Update 3D Queue UI
    update3DQueueUI(data);

    // Periodic catalog refresh
    if ((data.completed_2d + data.completed_3d) % 2 === 0) {
        loadCatalog(currentCatalogId);
    }
}

function update2DQueueUI(data) {
    const total = data.total_2d || 0;
    const completed = data.completed_2d || 0;
    const failed = data.failed_2d || 0;
    const isRunning = data.is_running_2d;
    const current = data.current_2d;
    const pending = data.pending_2d || [];
    const pendingCount = data.pending_count_2d || 0;

    // Status
    if (isRunning) {
        queue2DStatus.textContent = `Running (${completed}/${total})`;
        queue2DStatus.className = 'queue-status running';
    } else if (total > 0 && completed + failed >= total) {
        queue2DStatus.textContent = `Done (${completed} OK / ${failed} Fail)`;
        queue2DStatus.className = 'queue-status complete';
    } else if (total === 0) {
        queue2DStatus.textContent = 'Idle';
        queue2DStatus.className = 'queue-status';
    }

    // Progress bar
    const percent = total > 0 ? Math.round((completed + failed) / total * 100) : 0;
    queue2DProgress.style.width = `${percent}%`;

    // Current task
    if (current) {
        const elapsed = current.started_at ? getElapsedTime(current.started_at) : '';
        queue2DCurrent.innerHTML = `
            <div class="current-task">
                <span class="current-icon">&#x1F3A8;</span>
                <span class="current-name">${current.asset_name}</span>
                ${elapsed ? `<span class="current-time">${elapsed}</span>` : ''}
            </div>
        `;
    } else {
        queue2DCurrent.innerHTML = '';
    }

    // Pending list
    if (pending.length > 0) {
        let pendingHtml = '<div class="pending-header">Pending:</div><ul class="pending-list">';
        for (const name of pending) {
            pendingHtml += `<li>${name}</li>`;
        }
        if (pendingCount > pending.length) {
            pendingHtml += `<li class="more">+${pendingCount - pending.length} more...</li>`;
        }
        pendingHtml += '</ul>';
        queue2DPending.innerHTML = pendingHtml;
    } else {
        queue2DPending.innerHTML = '';
    }
}

function update3DQueueUI(data) {
    const total = data.total_3d || 0;
    const completed = data.completed_3d || 0;
    const failed = data.failed_3d || 0;
    const isRunning = data.is_running_3d;
    const current = data.current_3d;
    const pending = data.pending_3d || [];
    const pendingCount = data.pending_count_3d || 0;

    // Status
    if (isRunning) {
        queue3DStatus.textContent = `Running (${completed}/${total})`;
        queue3DStatus.className = 'queue-status running';
    } else if (total > 0 && completed + failed >= total) {
        queue3DStatus.textContent = `Done (${completed} OK / ${failed} Fail)`;
        queue3DStatus.className = 'queue-status complete';
    } else if (total === 0) {
        queue3DStatus.textContent = 'Idle';
        queue3DStatus.className = 'queue-status';
    }

    // Progress bar
    const percent = total > 0 ? Math.round((completed + failed) / total * 100) : 0;
    queue3DProgress.style.width = `${percent}%`;

    // Current task
    if (current) {
        const elapsed = current.started_at ? getElapsedTime(current.started_at) : '';
        queue3DCurrent.innerHTML = `
            <div class="current-task">
                <span class="current-icon">&#x1F4E6;</span>
                <span class="current-name">${current.asset_name}</span>
                ${elapsed ? `<span class="current-time">${elapsed}</span>` : ''}
            </div>
        `;
    } else {
        queue3DCurrent.innerHTML = '';
    }

    // Pending list
    if (pending.length > 0) {
        let pendingHtml = '<div class="pending-header">Pending:</div><ul class="pending-list">';
        for (const name of pending) {
            pendingHtml += `<li>${name}</li>`;
        }
        if (pendingCount > pending.length) {
            pendingHtml += `<li class="more">+${pendingCount - pending.length} more...</li>`;
        }
        pendingHtml += '</ul>';
        queue3DPending.innerHTML = pendingHtml;
    } else {
        queue3DPending.innerHTML = '';
    }
}

function getElapsedTime(isoString) {
    const start = new Date(isoString);
    const elapsed = Math.round((Date.now() - start.getTime()) / 1000);
    if (elapsed < 60) return `${elapsed}s`;
    const mins = Math.floor(elapsed / 60);
    const secs = elapsed % 60;
    return `${mins}m ${secs}s`;
}

function resetQueueUI() {
    queue2DStatus.textContent = 'Waiting...';
    queue2DStatus.className = 'queue-status';
    queue2DCurrent.innerHTML = '';
    queue2DProgress.style.width = '0%';
    queue2DPending.innerHTML = '';

    queue3DStatus.textContent = 'Waiting...';
    queue3DStatus.className = 'queue-status';
    queue3DCurrent.innerHTML = '';
    queue3DProgress.style.width = '0%';
    queue3DPending.innerHTML = '';
}

function resetButtons() {
    generateAllBtn.disabled = false;
    generate2DBtn.disabled = false;
    generate3DBtn.disabled = false;
}

// === Individual Asset Actions ===

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

async function generate2DAsset(assetId) {
    try {
        const btn = event.target;
        btn.disabled = true;
        btn.textContent = '2D...';

        await api.generateAsset2D(assetId);
        await loadCatalog(currentCatalogId);
    } catch (error) {
        alert('2D generation failed: ' + error.message);
        await loadCatalog(currentCatalogId);
    }
}

async function generate3DAsset(assetId) {
    try {
        const btn = event.target;
        btn.disabled = true;
        btn.textContent = '3D...';

        await api.generateAsset3D(assetId);
        await loadCatalog(currentCatalogId);
    } catch (error) {
        alert('3D generation failed: ' + error.message);
        await loadCatalog(currentCatalogId);
    }
}

async function viewAsset(assetId) {
    const asset = currentAssets.find(a => a.id === assetId);
    if (!asset || !asset.model_glb_url) return;

    selectedAssetId = assetId;

    // Expand viewer panel
    if (viewerPanel.classList.contains('collapsed')) {
        viewerPanel.classList.remove('collapsed');
        toggleViewerBtn.innerHTML = '&#9660;';
    }

    // Wait for DOM update
    await new Promise(resolve => requestAnimationFrame(resolve));

    try {
        await viewer3d.loadModel(asset.model_glb_url);
    } catch (error) {
        alert('3D model load failed: ' + error.message);
    }
}

async function deleteAsset(assetId) {
    if (!confirm('Delete this asset?')) return;

    try {
        await api.deleteAsset(assetId);
        await loadCatalog(currentCatalogId);
    } catch (error) {
        alert('Asset delete failed: ' + error.message);
    }
}
