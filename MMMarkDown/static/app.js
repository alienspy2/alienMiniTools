const state = {
  data: null,
  selectedId: null,
  seenNodes: new Set(),
  initialScrollDone: false,
  cutNodeId: null,
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
const X_SPACING = 320;
const Y_SPACING = 230;
const PADDING = 120;

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
};

function showToast(message, isError = false) {
  elements.toast.textContent = message;
  elements.toast.style.background = isError ? "#a03f1e" : "#2b2620";
  elements.toast.classList.add("visible");
  setTimeout(() => elements.toast.classList.remove("visible"), 2200);
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
  state.panPadding = { x: slackX, y: slackY };
  if (elements.mapViewport) {
    elements.mapViewport.style.width = `${scrollWidth}px`;
    elements.mapViewport.style.height = `${scrollHeight}px`;
  }
  elements.map.style.transform = `scale(${zoom})`;
  elements.links.style.transform = `scale(${zoom})`;
  elements.map.style.left = `${slackX}px`;
  elements.map.style.top = `${slackY}px`;
  elements.links.style.left = `${slackX}px`;
  elements.links.style.top = `${slackY}px`;
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

function updateMinimapViewport() {
  const viewport = state.minimapViewport;
  const base = state.baseSize;
  if (!viewport || !base) {
    return;
  }
  const scroll = elements.mapScroll;
  const zoom = state.zoom;
  const padding = state.panPadding || { x: 0, y: 0 };
  const viewWidth = Math.min(scroll.clientWidth / zoom, base.width);
  const viewHeight = Math.min(scroll.clientHeight / zoom, base.height);
  const maxX = Math.max(0, base.width - viewWidth);
  const maxY = Math.max(0, base.height - viewHeight);
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
  if (!state.baseSize || !elements.minimap) {
    return;
  }
  const svg = elements.minimap;
  const point = svg.createSVGPoint();
  point.x = event.clientX;
  point.y = event.clientY;
  const ctm = svg.getScreenCTM();
  if (!ctm) {
    return;
  }
  const svgPoint = point.matrixTransform(ctm.inverse());
  const scroll = elements.mapScroll;
  const zoom = state.zoom;
  const padding = state.panPadding || { x: 0, y: 0 };
  const viewWidth = Math.min(scroll.clientWidth / zoom, state.baseSize.width);
  const viewHeight = Math.min(scroll.clientHeight / zoom, state.baseSize.height);
  const maxX = Math.max(0, state.baseSize.width - viewWidth);
  const maxY = Math.max(0, state.baseSize.height - viewHeight);
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

function render() {
  const data = state.data;
  if (!data) {
    return;
  }
  const nodes = data.nodes || {};
  const edges = data.edges || [];
  const previousPositions = state.lastPositions || new Map();
  const nextPositions = new Map();
  const { positions, bounds } = layoutForest(data.root, nodes, edges);

  const width = Math.max(bounds.maxX - bounds.minX + PADDING * 2, elements.mapScroll.clientWidth);
  const height = Math.max(bounds.maxY - bounds.minY + PADDING * 2, elements.mapScroll.clientHeight);
  state.baseSize = { width, height };

  elements.map.style.width = `${width}px`;
  elements.map.style.height = `${height}px`;
  elements.links.setAttribute("width", width);
  elements.links.setAttribute("height", height);
  updateZoomTransforms();
  elements.links.innerHTML = "";
  elements.map.innerHTML = "";

  const offsetX = PADDING - bounds.minX;
  const offsetY = PADDING - bounds.minY;
  state.lastLayout = { positions, offsetX, offsetY };

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

    if (animate) {
      requestAnimationFrame(() => {
        card.style.setProperty("--tx", "0px");
        card.style.setProperty("--ty", "0px");
      });
    }
  });

  state.lastPositions = nextPositions;
  renderMinimap(nodes, edges, positions, width, height);
  renderSidePanel();
  if (!state.initialScrollDone) {
    centerOnSelected(positions, offsetX, offsetY);
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
  const name = window.prompt("New node name");
  if (!name) {
    return;
  }
  try {
    const result = await apiPost("/api/node/create", {
      parent_id: state.selectedId,
      name,
    });
    applyState(result.state);
    showToast("Node created.");
  } catch (error) {
    showToast(error.message, true);
  }
}

async function reorderSelected(direction) {
  if (!state.selectedId) {
    return;
  }
  try {
    const result = await apiPost("/api/node/reorder", {
      node_id: state.selectedId,
      direction,
    });
    applyState(result.state);
    showToast(direction === "up" ? "Moved up." : "Moved down.");
  } catch (error) {
    showToast(error.message, true);
  }
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
  const name = window.prompt("New sibling node name");
  if (!name) {
    return;
  }
  try {
    const result = await apiPost("/api/node/create", {
      parent_id: parentId,
      name,
    });
    applyState(result.state);
    showToast("Sibling node created.");
  } catch (error) {
    showToast(error.message, true);
  }
}

async function deleteSelected() {
  if (!state.selectedId) {
    return;
  }
  try {
    const result = await apiPost("/api/node/delete", {
      node_id: state.selectedId,
    });
    applyState(result.state);
    showToast("Node deleted.");
  } catch (error) {
    showToast(error.message, true);
  }
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
  try {
    const cutId = state.cutNodeId;
    const result = await apiPost("/api/node/move", {
      node_id: cutId,
      new_parent_id: state.selectedId,
    });
    state.cutNodeId = null;
    state.selectedId = cutId;
    applyState(result.state);
    showToast("Node pasted.");
  } catch (error) {
    showToast(error.message, true);
  }
}

async function renameSelected() {
  if (!state.selectedId) {
    return;
  }
  const currentName = state.data?.nodes?.[state.selectedId]?.name || "";
  const name = window.prompt("Rename node", currentName);
  if (!name || name === currentName) {
    return;
  }
  try {
    const result = await apiPost("/api/node/rename", {
      node_id: state.selectedId,
      name,
    });
    applyState(result.state);
    showToast("Node renamed.");
  } catch (error) {
    showToast(error.message, true);
  }
}

async function openSelected() {
  if (!state.selectedId) {
    return;
  }
  try {
    await apiPost("/api/node/open", { node_id: state.selectedId });
    showToast("Opened in VS Code.");
  } catch (error) {
    showToast(error.message, true);
  }
}

function handleKeyDown(event) {
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

elements.newChildBtn.addEventListener("click", createChild);
elements.renameBtn.addEventListener("click", renameSelected);
elements.openBtn.addEventListener("click", openSelected);
elements.mapScroll.addEventListener("wheel", handleWheel, { passive: false });
elements.mapScroll.addEventListener("mousedown", startPan);
elements.mapScroll.addEventListener("scroll", updateMinimapViewport);
window.addEventListener("mousemove", handlePanMove);
window.addEventListener("mouseup", endPan);
if (elements.minimap) {
  elements.minimap.addEventListener("click", handleMinimapClick);
}
if (elements.zoomInBtn) {
  elements.zoomInBtn.addEventListener("click", zoomIn);
}
if (elements.zoomOutBtn) {
  elements.zoomOutBtn.addEventListener("click", zoomOut);
}
if (elements.zoomFitBtn) {
  elements.zoomFitBtn.addEventListener("click", zoomToFit);
}
document.addEventListener("keydown", handleKeyDown);

fetchState();
setInterval(fetchState, 3000);
