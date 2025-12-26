const state = {
  data: null,
  selectedId: null,
  seenNodes: new Set(),
  initialScrollDone: false,
  cutNodeId: null,
  isModalOpen: false,
  lastPositions: new Map(),
  lastLayout: null,
  baseSize: null,
  zoom: 1,
  minimapViewport: null,
  isPanning: false,
  panStart: null,
  panPadding: { x: 0, y: 0 },
};

const NODE_WIDTH = 260;
const NODE_HEIGHT = 170;
const COMPACT_NODE_HEIGHT = 72;
const X_SPACING = 320;
const Y_SPACING = 230;
const PADDING = 120;
const PAN_PADDING = 180;

const MIN_ZOOM = 0.4;
const MAX_ZOOM = 2.5;
const ZOOM_STEP = 1.1;

const elements = {
  map: document.getElementById("map"),
  mapScroll: document.getElementById("map-scroll"),
  links: document.getElementById("links"),
  mapViewport: document.getElementById("map-viewport"),
  minimap: document.getElementById("minimap"),
  zoomInBtn: document.getElementById("zoom-in"),
  zoomOutBtn: document.getElementById("zoom-out"),
  zoomFitBtn: document.getElementById("zoom-fit"),
  selectedName: document.getElementById("selected-name"),
  selectedFile: document.getElementById("selected-file"),
  stateFile: document.getElementById("state-file"),
  toast: document.getElementById("toast"),
  newChildBtn: document.getElementById("new-child-btn"),
  renameBtn: document.getElementById("rename-btn"),
  openBtn: document.getElementById("open-btn"),
  refreshSummariesBtn: document.getElementById("refresh-summaries-btn"),
  realignFoldersBtn: document.getElementById("realign-folders-btn"),
  editorSelect: document.getElementById("editor-select"),
  modalOverlay: document.getElementById("modal-overlay"),
  modalTitle: document.getElementById("modal-title"),
  modalInput: document.getElementById("modal-input"),
  modalOk: document.getElementById("modal-ok"),
  modalCancel: document.getElementById("modal-cancel"),
};

let modalResolver = null;

function openNamePrompt(title, initialValue = "") {
  return new Promise((resolve) => {
    if (!elements.modalOverlay || !elements.modalInput || !elements.modalTitle) {
      showToast("Name prompt unavailable", true);
      resolve(null);
      return;
    }
    if (modalResolver) {
      modalResolver(null);
    }
    modalResolver = resolve;
    state.isModalOpen = true;
    elements.modalTitle.textContent = title;
    elements.modalInput.value = initialValue || "";
    elements.modalOverlay.classList.add("visible");
    requestAnimationFrame(() => {
      elements.modalInput.focus();
      elements.modalInput.select();
    });
  });
}

function closeNamePrompt(value) {
  if (!elements.modalOverlay) {
    return;
  }
  elements.modalOverlay.classList.remove("visible");
  state.isModalOpen = false;
  const resolve = modalResolver;
  modalResolver = null;
  if (resolve) {
    resolve(value);
  }
}

function confirmNamePrompt() {
  if (!elements.modalInput) {
    closeNamePrompt(null);
    return;
  }
  const value = elements.modalInput.value.trim();
  closeNamePrompt(value.length ? value : null);
}
function applyEditorSettings(settings) {
  if (!elements.editorSelect) {
    return;
  }
  const editor = settings?.editor || "vscode";
  elements.editorSelect.value = editor;
}

async function saveEditorSettings() {
  if (!elements.editorSelect) {
    return;
  }
  const editor = elements.editorSelect.value || "vscode";
  try {
    await apiPost("/api/settings/editor", { editor });
  } catch (error) {
    showToast(error.message, true);
  }
}

function showToast(message, isError = false) {
  elements.toast.textContent = message;
  elements.toast.style.background = isError ? "#a03f1e" : "#2b2620";
  elements.toast.classList.add("visible");
  setTimeout(() => elements.toast.classList.remove("visible"), 2200);
}

function on(element, eventName, handler, options) {
  if (!element) {
    return;
  }
  element.addEventListener(eventName, handler, options);
}
function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function updateZoomTransforms() {
  if (!state.baseSize) {
    return;
  }
  const zoom = state.zoom;
  const base = state.baseSize;
  const scrollScale = zoom < 1 ? 1 / zoom : zoom;
  const scrollWidth = base.width * scrollScale;
  const scrollHeight = base.height * scrollScale;
  const scaledWidth = base.width * zoom;
  const scaledHeight = base.height * zoom;
  const slackX = Math.max(0, (scrollWidth - scaledWidth) / 2);
  const slackY = Math.max(0, (scrollHeight - scaledHeight) / 2);
  state.panPadding = { x: slackX + PAN_PADDING, y: slackY + PAN_PADDING };
  if (elements.mapViewport) {
    elements.mapViewport.style.width = `${scrollWidth + PAN_PADDING * 2}px`;
    elements.mapViewport.style.height = `${scrollHeight + PAN_PADDING * 2}px`;
  }
  elements.map.style.transform = `scale(${zoom})`;
  elements.links.style.transform = `scale(${zoom})`;
  elements.map.style.left = `${slackX + PAN_PADDING}px`;
  elements.map.style.top = `${slackY + PAN_PADDING}px`;
  elements.links.style.left = `${slackX + PAN_PADDING}px`;
  elements.links.style.top = `${slackY + PAN_PADDING}px`;
}

function setZoom(nextZoom, clientPoint) {
  const zoom = clamp(nextZoom, MIN_ZOOM, MAX_ZOOM);
  if (zoom === state.zoom) {
    return;
  }
  const scroll = elements.mapScroll;
  const rect = scroll.getBoundingClientRect();
  const pointX = clientPoint ? clientPoint.x - rect.left : scroll.clientWidth / 2;
  const pointY = clientPoint ? clientPoint.y - rect.top : scroll.clientHeight / 2;
  const prevZoom = state.zoom;
  const prevPadding = state.panPadding || { x: 0, y: 0 };
  const contentX = (scroll.scrollLeft + pointX - prevPadding.x) / prevZoom;
  const contentY = (scroll.scrollTop + pointY - prevPadding.y) / prevZoom;

  state.zoom = zoom;
  updateZoomTransforms();
  const nextPadding = state.panPadding || { x: 0, y: 0 };

  scroll.scrollLeft = contentX * zoom + nextPadding.x - pointX;
  scroll.scrollTop = contentY * zoom + nextPadding.y - pointY;
  updateMinimapViewport();
}

function zoomIn() {
  setZoom(state.zoom * ZOOM_STEP);
}

function zoomOut() {
  setZoom(state.zoom / ZOOM_STEP);
}

function zoomToFit() {
  if (!state.baseSize) {
    return;
  }
  const scroll = elements.mapScroll;
  const fitZoom = Math.min(scroll.clientWidth / state.baseSize.width, scroll.clientHeight / state.baseSize.height);
  state.zoom = clamp(fitZoom, MIN_ZOOM, MAX_ZOOM);
  updateZoomTransforms();
  const scrollWidth = elements.mapViewport ? elements.mapViewport.offsetWidth : state.baseSize.width * state.zoom;
  const scrollHeight = elements.mapViewport ? elements.mapViewport.offsetHeight : state.baseSize.height * state.zoom;
  scroll.scrollLeft = Math.max(0, (scrollWidth - scroll.clientWidth) / 2);
  scroll.scrollTop = Math.max(0, (scrollHeight - scroll.clientHeight) / 2);
  updateMinimapViewport();
}

function getViewportMetrics() {
  const base = state.baseSize;
  const scroll = elements.mapScroll;
  if (!base || !scroll) {
    return null;
  }
  const zoom = state.zoom;
  const padding = state.panPadding || { x: 0, y: 0 };
  const viewWidth = Math.min(scroll.clientWidth / zoom, base.width);
  const viewHeight = Math.min(scroll.clientHeight / zoom, base.height);
  const maxX = Math.max(0, base.width - viewWidth);
  const maxY = Math.max(0, base.height - viewHeight);
  return { scroll, zoom, padding, viewWidth, viewHeight, maxX, maxY };
}

function updateMinimapViewport() {
  const viewport = state.minimapViewport;
  const metrics = getViewportMetrics();
  if (!viewport || !metrics) {
    return;
  }
  const { scroll, zoom, padding, viewWidth, viewHeight, maxX, maxY } = metrics;
  const viewX = clamp((scroll.scrollLeft - padding.x) / zoom, 0, maxX);
  const viewY = clamp((scroll.scrollTop - padding.y) / zoom, 0, maxY);
  viewport.setAttribute("x", viewX);
  viewport.setAttribute("y", viewY);
  viewport.setAttribute("width", viewWidth);
  viewport.setAttribute("height", viewHeight);
}

function renderMinimap(nodes, edges, positions, width, height) {
  if (!elements.minimap) {
    return;
  }
  if (!width || !height) {
    return;
  }
  const svg = elements.minimap;
  svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
  svg.setAttribute("preserveAspectRatio", "xMidYMid meet");
  svg.innerHTML = "";
  const svgNS = "http://www.w3.org/2000/svg";
  const edgeGroup = document.createElementNS(svgNS, "g");
  edges.forEach((edge) => {
    const from = positions[edge.from];
    const to = positions[edge.to];
    if (!from || !to) {
      return;
    }
    const line = document.createElementNS(svgNS, "line");
    line.setAttribute("x1", from.x + NODE_WIDTH / 2);
    line.setAttribute("y1", from.y + NODE_HEIGHT / 2);
    line.setAttribute("x2", to.x + NODE_WIDTH / 2);
    line.setAttribute("y2", to.y + NODE_HEIGHT / 2);
    line.setAttribute("class", "minimap-edge");
    line.setAttribute("pointer-events", "none");
    edgeGroup.appendChild(line);
  });
  const nodeGroup = document.createElementNS(svgNS, "g");
  Object.values(nodes).forEach((node) => {
    const pos = positions[node.id];
    if (!pos) {
      return;
    }
    const rect = document.createElementNS(svgNS, "rect");
    rect.setAttribute("x", pos.x);
    rect.setAttribute("y", pos.y);
    rect.setAttribute("width", NODE_WIDTH);
    rect.setAttribute("height", NODE_HEIGHT);
    rect.setAttribute("rx", "10");
    rect.setAttribute("ry", "10");
    rect.setAttribute("class", node.id === state.selectedId ? "minimap-node minimap-node--selected" : "minimap-node");
    rect.setAttribute("pointer-events", "none");
    nodeGroup.appendChild(rect);
  });
  const viewport = document.createElementNS(svgNS, "rect");
  viewport.setAttribute("id", "minimap-viewport");
  viewport.setAttribute("class", "minimap-viewport");
  svg.appendChild(edgeGroup);
  svg.appendChild(nodeGroup);
  svg.appendChild(viewport);
  state.minimapViewport = viewport;
  updateMinimapViewport();
}

function handleMinimapClick(event) {
  if (!elements.minimap) {
    return;
  }
  const metrics = getViewportMetrics();
  if (!metrics) {
    return;
  }
  const { scroll, zoom, padding, viewWidth, viewHeight, maxX, maxY } = metrics;
  const svg = elements.minimap;
  const point = svg.createSVGPoint();
  point.x = event.clientX;
  point.y = event.clientY;
  const ctm = svg.getScreenCTM();
  if (!ctm) {
    return;
  }
  const svgPoint = point.matrixTransform(ctm.inverse());
  const targetX = clamp(svgPoint.x - viewWidth / 2, 0, maxX);
  const targetY = clamp(svgPoint.y - viewHeight / 2, 0, maxY);
  scroll.scrollLeft = targetX * zoom + padding.x;
  scroll.scrollTop = targetY * zoom + padding.y;
  updateMinimapViewport();
}

function handleWheel(event) {
  if (!event.ctrlKey) {
    return;
  }
  event.preventDefault();
  const zoomFactor = event.deltaY > 0 ? 1 / ZOOM_STEP : ZOOM_STEP;
  setZoom(state.zoom * zoomFactor, { x: event.clientX, y: event.clientY });
}

function startPan(event) {
  if (event.button !== 1) {
    return;
  }
  event.preventDefault();
  state.isPanning = true;
  state.panStart = {
    x: event.clientX,
    y: event.clientY,
    scrollLeft: elements.mapScroll.scrollLeft,
    scrollTop: elements.mapScroll.scrollTop,
  };
  elements.mapScroll.classList.add("panning");
}

function handlePanMove(event) {
  if (!state.isPanning || !state.panStart) {
    return;
  }
  const dx = event.clientX - state.panStart.x;
  const dy = event.clientY - state.panStart.y;
  elements.mapScroll.scrollLeft = state.panStart.scrollLeft - dx;
  elements.mapScroll.scrollTop = state.panStart.scrollTop - dy;
}

function endPan() {
  if (!state.isPanning) {
    return;
  }
  state.isPanning = false;
  state.panStart = null;
  elements.mapScroll.classList.remove("panning");
}

async function apiPost(path, payload) {
  const response = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  const data = await response.json();
  if (!data.ok) {
    throw new Error(data.error || "Request failed");
  }
  return data;
}

async function runAction(action, options = {}) {
  const { successMessage, onSuccess } = options;
  try {
    const result = await action();
    if (onSuccess) {
      onSuccess(result);
    }
    if (result?.state) {
      applyState(result.state);
    }
    if (successMessage) {
      showToast(successMessage);
    }
    return result;
  } catch (error) {
    showToast(error.message, true);
    return null;
  }
}
async function fetchState() {
  try {
    const response = await fetch("/api/state");
    const data = await response.json();
    if (!data.ok) {
      throw new Error(data.error || "Failed to load state");
    }
    applyState(data);
  } catch (error) {
    showToast(error.message, true);
  }
}

function applyState(data) {
  state.data = data;
  elements.stateFile.textContent = `State: ${data.state_file || "-"}`;

  const nodes = data.nodes || {};
  if (state.cutNodeId && !nodes[state.cutNodeId]) {
    state.cutNodeId = null;
  }
  if (!state.selectedId || !nodes[state.selectedId]) {
    state.selectedId = data.root || Object.keys(nodes)[0] || null;
  }
  applyEditorSettings(data.settings || {});
  render();
}

function buildChildrenMap(edges) {
  const children = new Map();
  edges.forEach((edge) => {
    if (!children.has(edge.from)) {
      children.set(edge.from, []);
    }
    children.get(edge.from).push(edge.to);
  });
  return children;
}

function buildVisibleGraph(nodes, edges, rootId) {
  const childrenMap = buildChildrenMap(edges);
  const parentMap = new Map();
  edges.forEach((edge) => {
    parentMap.set(edge.to, edge.from);
  });
  const visibleNodes = {};
  const visibleEdges = [];
  const visited = new Set();
  const roots = [];

  if (rootId && nodes[rootId]) {
    roots.push(rootId);
  }
  Object.keys(nodes).forEach((nodeId) => {
    if (!parentMap.has(nodeId) && !roots.includes(nodeId)) {
      roots.push(nodeId);
    }
  });

  function walk(nodeId) {
    if (visited.has(nodeId)) {
      return;
    }
    const node = nodes[nodeId];
    if (!node) {
      return;
    }
    visited.add(nodeId);
    visibleNodes[nodeId] = node;
    const children = childrenMap.get(nodeId) || [];
    if (node.collapsed) {
      return;
    }
    children.forEach((childId) => {
      if (!nodes[childId]) {
        return;
      }
      visibleEdges.push({ from: nodeId, to: childId });
      walk(childId);
    });
  }

  roots.forEach((nodeId) => {
    walk(nodeId);
  });

  return { nodes: visibleNodes, edges: visibleEdges, childrenMap };
}

function layoutForest(rootId, nodes, edges) {
  const childrenMap = buildChildrenMap(edges);
  const positions = {};
  const visited = new Set();

  function walk(nodeId, depth, yTop) {
    if (visited.has(nodeId)) {
      return { height: Y_SPACING, center: yTop };
    }
    visited.add(nodeId);
    const children = childrenMap.get(nodeId) || [];
    if (children.length === 0) {
      positions[nodeId] = { x: depth * X_SPACING, y: yTop };
      return { height: Y_SPACING, center: yTop };
    }
    let currentY = yTop;
    const centers = [];
    for (const childId of children) {
      const result = walk(childId, depth + 1, currentY);
      centers.push(result.center);
      currentY += result.height;
    }
    if (centers.length === 0) {
      positions[nodeId] = { x: depth * X_SPACING, y: yTop };
      return { height: Y_SPACING, center: yTop };
    }
    const center = (centers[0] + centers[centers.length - 1]) / 2;
    positions[nodeId] = { x: depth * X_SPACING, y: center };
    return { height: Math.max(currentY - yTop, Y_SPACING), center };
  }

  const nodeIds = Object.keys(nodes);
  if (nodeIds.length === 0) {
    return { positions, bounds: { minX: 0, minY: 0, maxX: NODE_WIDTH, maxY: NODE_HEIGHT } };
  }

  const roots = [];
  if (rootId && nodes[rootId]) {
    roots.push(rootId);
  }
  for (const nodeId of nodeIds) {
    if (!roots.includes(nodeId)) {
      roots.push(nodeId);
    }
  }

  let yOffset = 0;
  roots.forEach((nodeId) => {
    if (visited.has(nodeId)) {
      return;
    }
    const result = walk(nodeId, 0, yOffset);
    yOffset += result.height + Y_SPACING;
  });

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;
  Object.values(positions).forEach((pos) => {
    minX = Math.min(minX, pos.x);
    minY = Math.min(minY, pos.y);
    maxX = Math.max(maxX, pos.x + NODE_WIDTH);
    maxY = Math.max(maxY, pos.y + NODE_HEIGHT);
  });

  return { positions, bounds: { minX, minY, maxX, maxY } };
}

function buildLayout(data) {
  const nodes = data.nodes || {};
  const edges = data.edges || [];
  const { positions, bounds } = layoutForest(data.root, nodes, edges);
  const width = Math.max(bounds.maxX - bounds.minX + PADDING * 2, elements.mapScroll.clientWidth);
  const height = Math.max(bounds.maxY - bounds.minY + PADDING * 2, elements.mapScroll.clientHeight);
  const offsetX = PADDING - bounds.minX;
  const offsetY = PADDING - bounds.minY;
  return { nodes, edges, positions, bounds, width, height, offsetX, offsetY };
}

function applyCanvasSize(width, height) {
  state.baseSize = { width, height };
  elements.map.style.width = `${width}px`;
  elements.map.style.height = `${height}px`;
  elements.links.setAttribute("width", width);
  elements.links.setAttribute("height", height);
  updateZoomTransforms();
}

function renderEdges(edges, positions, offsetX, offsetY) {
  edges.forEach((edge) => {
    const from = positions[edge.from];
    const to = positions[edge.to];
    if (!from || !to) {
      return;
    }
    const startX = from.x + offsetX + NODE_WIDTH;
    const startY = from.y + offsetY + NODE_HEIGHT / 2;
    const endX = to.x + offsetX;
    const endY = to.y + offsetY + NODE_HEIGHT / 2;
    const midX = (startX + endX) / 2;

    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", `M ${startX} ${startY} C ${midX} ${startY}, ${midX} ${endY}, ${endX} ${endY}`);
    path.setAttribute("fill", "none");
    path.setAttribute("stroke", "rgba(51, 39, 26, 0.3)");
    path.setAttribute("stroke-width", "2");
    elements.links.appendChild(path);
  });
}

function renderNodes(nodes, positions, offsetX, offsetY, previousPositions, childrenMap) {
  const nextPositions = new Map();
  const nodeList = Object.values(nodes).sort((a, b) => a.name.localeCompare(b.name));
  nodeList.forEach((node, index) => {
    const pos = positions[node.id];
    if (!pos) {
      return;
    }

    const card = document.createElement("div");
    card.className = "node";
    if (node.id === state.selectedId) {
      card.classList.add("node--selected");
    }
    const isNew = !state.seenNodes.has(node.id);
    if (isNew) {
      card.classList.add("node--new");
      state.seenNodes.add(node.id);
    }
    const targetX = pos.x + offsetX;
    const targetY = pos.y + offsetY;
    card.style.left = `${targetX}px`;
    card.style.top = `${targetY}px`;
    card.style.animationDelay = `${index * 40}ms`;
    card.dataset.id = node.id;
    nextPositions.set(node.id, { x: targetX, y: targetY });

    const title = document.createElement("div");
    title.className = "node-title";
    title.textContent = node.name;

    const summary = document.createElement("div");
    summary.className = "node-summary";
    summary.textContent = node.summary || "";

    card.appendChild(title);
    card.appendChild(summary);
    if (!summary.textContent.trim()) {
      card.classList.add("node--compact");
      card.style.setProperty("--cy", `${(NODE_HEIGHT - COMPACT_NODE_HEIGHT) / 2}px`);
    }
    const children = childrenMap.get(node.id) || [];
    if (children.length) {
      const isCollapsed = Boolean(node.collapsed);
      const toggle = document.createElement("button");
      toggle.type = "button";
      toggle.className = "node-toggle";
      toggle.textContent = isCollapsed ? "+" : "-";
      toggle.title = isCollapsed ? "Expand" : "Collapse";
      toggle.addEventListener("click", (event) => {
        event.stopPropagation();
        toggleNodeCollapse(node.id, !isCollapsed);
      });
      card.appendChild(toggle);
      if (isCollapsed) {
        card.classList.add("node--collapsed");
      }
    }
    card.addEventListener("click", () => {
      state.selectedId = node.id;
      render();
    });
    card.addEventListener("dblclick", () => {
      openSelected();
    });

    const prevPos = previousPositions.get(node.id);
    let animate = false;
    if (prevPos && !isNew) {
      const dx = prevPos.x - targetX;
      const dy = prevPos.y - targetY;
      if (dx !== 0 || dy !== 0) {
        animate = true;
        card.style.setProperty("--tx", `${dx}px`);
        card.style.setProperty("--ty", `${dy}px`);
      }
    }
    if (!animate) {
      card.style.setProperty("--tx", "0px");
      card.style.setProperty("--ty", "0px");
    }

    elements.map.appendChild(card);
    if (card.classList.contains("node--compact")) {
      const measured = card.offsetHeight;
      const offset = Math.max(0, (NODE_HEIGHT - measured) / 2);
      card.style.setProperty("--cy", `${offset}px`);
    }

    if (animate) {
      requestAnimationFrame(() => {
        card.style.setProperty("--tx", "0px");
        card.style.setProperty("--ty", "0px");
      });
    }
  });

  state.lastPositions = nextPositions;
}
function render() {
  const data = state.data;
  if (!data) {
    return;
  }
  const graph = buildVisibleGraph(data.nodes || {}, data.edges || [], data.root);
  if (!state.selectedId || !graph.nodes[state.selectedId]) {
    if (data.root && graph.nodes[data.root]) {
      state.selectedId = data.root;
    } else {
      state.selectedId = Object.keys(graph.nodes)[0] || null;
    }
  }
  const previousPositions = state.lastPositions || new Map();
  const layout = buildLayout({ root: data.root, nodes: graph.nodes, edges: graph.edges });

  applyCanvasSize(layout.width, layout.height);
  elements.links.innerHTML = "";
  elements.map.innerHTML = "";

  state.lastLayout = { positions: layout.positions, offsetX: layout.offsetX, offsetY: layout.offsetY };

  renderEdges(layout.edges, layout.positions, layout.offsetX, layout.offsetY);
  renderNodes(
    layout.nodes,
    layout.positions,
    layout.offsetX,
    layout.offsetY,
    previousPositions,
    graph.childrenMap
  );
  renderMinimap(layout.nodes, layout.edges, layout.positions, layout.width, layout.height);
  renderSidePanel();
  if (!state.initialScrollDone) {
    centerOnSelected(layout.positions, layout.offsetX, layout.offsetY);
    state.initialScrollDone = true;
  }
}

function renderSidePanel() {
  const nodes = state.data?.nodes || {};
  const node = nodes[state.selectedId];
  if (!node) {
    elements.selectedName.textContent = "-";
    elements.selectedFile.textContent = "-";
    return;
  }
  elements.selectedName.textContent = node.name;
  elements.selectedFile.textContent = node.file_abs || node.file || "-";
}

function centerOnSelected(positions, offsetX, offsetY) {
  const target = positions[state.selectedId];
  if (!target) {
    return;
  }
  const scroll = elements.mapScroll;
  const zoom = state.zoom;
  const padding = state.panPadding || { x: 0, y: 0 };
  const targetX = (target.x + offsetX + NODE_WIDTH / 2) * zoom + padding.x - scroll.clientWidth / 2;
  const targetY = (target.y + offsetY + NODE_HEIGHT / 2) * zoom + padding.y - scroll.clientHeight / 2;
  scroll.scrollLeft = Math.max(0, targetX);
  scroll.scrollTop = Math.max(0, targetY);
}

function focusSelected() {
  if (!state.lastLayout) {
    return;
  }
  centerOnSelected(state.lastLayout.positions, state.lastLayout.offsetX, state.lastLayout.offsetY);
  updateMinimapViewport();
}

async function createChild() {
  const name = await openNamePrompt("New node name");
  if (!name) {
    return;
  }
  await runAction(
    () => apiPost("/api/node/create", {
      parent_id: state.selectedId,
      name,
    }),
    { successMessage: "Node created." }
  );
}

async function reorderSelected(direction) {
  if (!state.selectedId) {
    return;
  }
  await runAction(
    () => apiPost("/api/node/reorder", {
      node_id: state.selectedId,
      direction,
    }),
    { successMessage: direction === "up" ? "Moved up." : "Moved down." }
  );
}

function findParentId(nodeId) {
  const edges = state.data?.edges || [];
  const edge = edges.find((item) => item.to === nodeId);
  return edge ? edge.from : state.data?.root || null;
}

async function createSibling() {
  if (!state.selectedId) {
    return;
  }
  const parentId = findParentId(state.selectedId);
  const name = await openNamePrompt("New sibling node name");
  if (!name) {
    return;
  }
  await runAction(
    () => apiPost("/api/node/create", {
      parent_id: parentId,
      insert_after_id: state.selectedId,
      name,
    }),
    { successMessage: "Sibling node created." }
  );
}

async function deleteSelected() {
  if (!state.selectedId) {
    return;
  }
  await runAction(() => apiPost("/api/node/delete", { node_id: state.selectedId }), {
    successMessage: "Node deleted.",
  });
}

function cutSelected() {
  const rootId = state.data?.root || null;
  if (!state.selectedId || state.selectedId === rootId) {
    showToast("Cannot cut root node.", true);
    return;
  }
  state.cutNodeId = state.selectedId;
  showToast("Node cut.");
}

async function pasteToSelected() {
  if (!state.cutNodeId || !state.selectedId) {
    return;
  }
  if (state.cutNodeId === state.selectedId) {
    showToast("Cannot paste into itself.", true);
    return;
  }
  const cutId = state.cutNodeId;
  await runAction(
    () => apiPost("/api/node/move", {
      node_id: cutId,
      new_parent_id: state.selectedId,
    }),
    {
      onSuccess: () => {
        state.cutNodeId = null;
        state.selectedId = cutId;
      },
      successMessage: "Node pasted.",
    }
  );
}

async function renameSelected() {
  if (!state.selectedId) {
    return;
  }
  const currentName = state.data?.nodes?.[state.selectedId]?.name || "";
  const name = await openNamePrompt("Rename node", currentName);
  if (!name || name === currentName) {
    return;
  }
  await runAction(
    () => apiPost("/api/node/rename", {
      node_id: state.selectedId,
      name,
    }),
    { successMessage: "Node renamed." }
  );
}

async function openSelected() {
  if (!state.selectedId) {
    return;
  }
  await runAction(() => apiPost("/api/node/open", { node_id: state.selectedId }), {
    successMessage: "Opened in editor.",
  });
}

async function refreshSummaries() {
  await runAction(() => apiPost("/api/summary/refresh", {}), {
    successMessage: "요약 재요청됨 (테스트용).",
  });
}

async function realignFolders() {
  await runAction(() => apiPost("/api/folders/realign", {}), {
    successMessage: "폴더 구조를 정렬했어요.",
  });
}

async function toggleNodeCollapse(nodeId, collapsed) {
  try {
    const result = await apiPost("/api/node/collapse", { node_id: nodeId, collapsed });
    if (result?.state) {
      state.selectedId = nodeId;
      applyState(result.state);
      if (!collapsed) {
        requestAnimationFrame(() => {
          focusSelected();
        });
      }
    }
  } catch (error) {
    showToast(error.message, true);
  }
}

function handleKeyDown(event) {
  if (state.isModalOpen) {
    return;
  }
  if (event.target.tagName === "INPUT" || event.target.tagName === "TEXTAREA") {
    return;
  }
  const metaKey = event.ctrlKey || event.metaKey;
  if (event.shiftKey && event.key === "ArrowUp") {
    event.preventDefault();
    reorderSelected("up");
    return;
  }
  if (event.shiftKey && event.key === "ArrowDown") {
    event.preventDefault();
    reorderSelected("down");
    return;
  }
  if (metaKey && event.key.toLowerCase() === "x") {
    event.preventDefault();
    cutSelected();
    return;
  }
  if (metaKey && event.key.toLowerCase() === "v") {
    event.preventDefault();
    pasteToSelected();
    return;
  }
  if (event.key === "Delete") {
    event.preventDefault();
    deleteSelected();
    return;
  }
  if (!metaKey && event.key.toLowerCase() === "f") {
    event.preventDefault();
    focusSelected();
    return;
  }
  if (event.key === "Insert") {
    event.preventDefault();
    createChild();
  }
  if (event.key === "Tab") {
    event.preventDefault();
    createSibling();
  }
  if (event.key === "F2" || event.key === "Enter") {
    event.preventDefault();
    openSelected();
  }
}

function bindEvents() {
  on(elements.newChildBtn, "click", createChild);
  on(elements.renameBtn, "click", renameSelected);
  on(elements.openBtn, "click", openSelected);
  on(elements.refreshSummariesBtn, "click", refreshSummaries);
  on(elements.realignFoldersBtn, "click", realignFolders);
  on(elements.editorSelect, "change", () => {
    saveEditorSettings();
  });
  on(elements.modalOverlay, "click", (event) => {
    if (event.target === elements.modalOverlay) {
      closeNamePrompt(null);
    }
  });
  on(elements.modalCancel, "click", () => {
    closeNamePrompt(null);
  });
  on(elements.modalOk, "click", confirmNamePrompt);
  on(elements.modalInput, "keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      confirmNamePrompt();
    }
    if (event.key === "Escape") {
      event.preventDefault();
      closeNamePrompt(null);
    }
  });
  on(elements.mapScroll, "wheel", handleWheel, { passive: false });
  on(elements.mapScroll, "mousedown", startPan);
  on(elements.mapScroll, "scroll", updateMinimapViewport);
  on(window, "mousemove", handlePanMove);
  on(window, "mouseup", endPan);
  on(elements.minimap, "click", handleMinimapClick);
  on(elements.zoomInBtn, "click", zoomIn);
  on(elements.zoomOutBtn, "click", zoomOut);
  on(elements.zoomFitBtn, "click", zoomToFit);
  on(document, "keydown", handleKeyDown);
}
bindEvents();

fetchState();
setInterval(fetchState, 3000);
