/**
 * three-module.js — Three.js ES Module for Liuvis
 *
 * All JSInterop functions that Blazor calls via IJSRuntime.InvokeVoidAsync / InvokeAsync.
 * Three.js interaction (OrbitControls, animation loop) runs entirely in-browser.
 *
 * Uses ES import maps (configured in index.html / App.razor) to load Three.js from CDN.
 *
 * Exports:
 *   initScene(containerId)              — Initialize Three.js scene in a div
 *   loadModel(glbUrl, force)            — Load a GLB model into the scene
 *   loadStlModel(stlUrl)               — Load STL and auto-separate components
 *   updateComponentColor(name, color)   — Change a component's material color
 *   highlightComponent(name)            — Flash highlight with emissive glow
 *   selectComponent(name)              — Persistent selection highlight
 *   deselectAllComponents()            — Clear all selection highlights
 *   toggleComponentVisibility(name, v) — Show/hide a component
 *   getComponentList()                 — Return component metadata as JSON
 *   setComponentColor(name, hex)       — Set persistent color for a component
 *   getSceneSnapshot()                 — Return current camera state as JSON
 *   dispose()                          — Clean up all resources
 *   resetLoadedUrl()                   — Clear URL cache to force reload
 */

import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { STLLoader } from 'three/addons/loaders/STLLoader.js';
import { EffectComposer } from 'three/addons/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/addons/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/addons/postprocessing/UnrealBloomPass.js';

// ----------------------------------------------------------------
// Module state
// ----------------------------------------------------------------
let _scene = null;
let _camera = null;
let _renderer = null;
let _controls = null;
let _composer = null;
let _bloomPass = null;
let _currentModel = null;
let _loadedUrl = null;
let _animationId = null;
let _componentMap = new Map();
let _containerElement = null;
let _frameCount = 0;
let _currentContainerId = null;
let _selectedComponent = null;
let _highlightTimeout = null;

// Component colors palette for auto-coloring separated components
const _COMPONENT_COLORS = [
    0x00d4ff, 0xff6b6b, 0x51cf66, 0xffd43b, 0xcc5de8,
    0x20c997, 0xf06595, 0x748ffc, 0xfab005, 0x63e6be,
    0xe8590c, 0x15aabf, 0xbe4bdb, 0x82c91e, 0x228be6
];

// ----------------------------------------------------------------
// Helpers
// ----------------------------------------------------------------
function _findObjectByName(root, name) {
    if (root.name === name) return root;
    for (const child of root.children) {
        const found = _findObjectByName(child, name);
        if (found) return found;
    }
    return null;
}

function _collectNamedObjects(root, map) {
    if (root.name && root.name.length > 0) {
        map.set(root.name, root);
    }
    for (const child of root.children) {
        _collectNamedObjects(child, map);
    }
}

// ----------------------------------------------------------------
// Connectivity Analysis — separate connected components in STL geometry
//
// Three.js STLLoader produces non-indexed geometry: every triangle has its
// own 3 unique vertex entries in the position buffer, even when the original
// STL file shares vertices across triangles.  This means naive vertex-hash
// matching finds no shared vertices — every triangle becomes an isolated node.
//
// Fix: weld vertices first by quantizing all positions to a grid, assigning
// a canonical index to each unique grid cell, then building triangle
// adjacency from these canonical vertex indices.

// ----------------------------------------------------------------
// _splitByTriangleCounts(pos, triangleCounts)
// Exact component separation using triangle count ranges.
// Triangles in the STL are stored in the order exported by ProceduralGeometryBuilder,
// so the first count[0] triangles belong to component 0, the next count[1] to component 1, etc.
// ----------------------------------------------------------------
function _splitByTriangleCounts(pos, triangleCounts) {
    const vertexCount = pos.count;
    const triangleCount = vertexCount / 3;
    const totalExpected = triangleCounts.reduce((a, b) => a + b, 0);

    console.log(`[three-module] _splitByTriangleCounts: ${triangleCounts.length} components, expected ${totalExpected} of ${triangleCount} triangles`);

    const result = [];
    let offset = 0;
    for (let i = 0; i < triangleCounts.length; i++) {
        const n = triangleCounts[i];
        if (offset + n > triangleCount) {
            console.warn(`[three-module] Component ${i}: count ${n} exceeds remaining triangles (${triangleCount - offset}), truncating`);
            break;
        }
        if (n < 4) {
            console.log(`[three-module] Component ${i}: ${n} triangles < 4, skipping`);
            offset += n;
            continue;
        }
        const triangles = Array.from({ length: n }, (_, j) => offset + j);
        result.push({
            triangles,
            name: triangleCounts.length === 1 ? 'MainBody' : `Component_${i + 1}`,
            triangleCount: n
        });
        offset += n;
    }

    console.log(`[three-module] Exact splitting: ${result.length} component(s) — ${result.map(c => `${c.name}:${c.triangleCount}`).join(', ')}`);
    return result;
}

// ----------------------------------------------------------------
// _separateStlComponents(geometry: THREE.BufferGeometry)
// Heuristic component separation using vertex-level normal clustering.
// Fallback for STL files without component index metadata (third-party STLs).
// ----------------------------------------------------------------
function _separateStlComponents(geometry) {
    const positions = geometry.getAttribute('position');
    const vertexCount = positions.count;
    const triangleCount = vertexCount / 3;

    if (triangleCount < 8) {
        return [{ triangles: [...Array(triangleCount).keys()], name: 'MainBody' }];
    }

    // ── Compute model bounding box to derive scale-adaptive epsilon ──
    let minX = Infinity, minY = Infinity, minZ = Infinity;
    let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
    for (let vi = 0; vi < vertexCount; vi++) {
        const x = positions.getX(vi), y = positions.getY(vi), z = positions.getZ(vi);
        if (x < minX) minX = x; if (x > maxX) maxX = x;
        if (y < minY) minY = y; if (y > maxY) maxY = y;
        if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
    }
    const modelSize = Math.max(maxX - minX, maxY - minY, maxZ - minZ, 1.0);
    // epsilon: 1e-6 * modelSize (1 part per million), with floor 1e-5 for tiny models
    const epsilon = Math.max(modelSize * 1e-6, 1e-7);
    const quantize = 1.0 / epsilon;
    console.log(`[three-module] Model size: ${modelSize.toFixed(2)}, epsilon: ${epsilon.toExponential(2)}, quantize: ${quantize.toExponential(2)}`);

    // ── Step 0: Vertex welding ──
    // Map each quantized position to a canonical vertex index.
    // String key avoids BigInt sign pitfalls with negative coordinates.
    const vertexKeyToIndex = new Map();
    const canonicalIdx = new Array(vertexCount);

    for (let vi = 0; vi < vertexCount; vi++) {
        const qx = Math.round(positions.getX(vi) * quantize);
        const qy = Math.round(positions.getY(vi) * quantize);
        const qz = Math.round(positions.getZ(vi) * quantize);
        const key = `${qx},${qy},${qz}`;

        if (!vertexKeyToIndex.has(key)) {
            vertexKeyToIndex.set(key, vertexKeyToIndex.size);
        }
        canonicalIdx[vi] = vertexKeyToIndex.get(key);
    }

    const uniqueVerts = vertexKeyToIndex.size;
    console.log(`[three-module] Vertex welding: ${vertexCount} raw → ${uniqueVerts} unique (${((1 - uniqueVerts / vertexCount) * 100).toFixed(1)}% dedup)`);

    // ── Build canonical vertex positions array ──
    const canonicalPositions = new Array(uniqueVerts);
    const seenCanonical = new Set();
    for (let vi = 0; vi < vertexCount; vi++) {
        const ci = canonicalIdx[vi];
        if (!seenCanonical.has(ci)) {
            seenCanonical.add(ci);
            canonicalPositions[ci] = [positions.getX(vi), positions.getY(vi), positions.getZ(vi)];
        }
    }

    // ── Canonical vertex → triangle set ──
    const vertToTris = new Array(uniqueVerts);
    for (let i = 0; i < uniqueVerts; i++) vertToTris[i] = new Set();
    for (let t = 0; t < triangleCount; t++) {
        for (let v = 0; v < 3; v++) {
            vertToTris[canonicalIdx[t * 3 + v]].add(t);
        }
    }

    // ── Vector helpers ──
    const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    const cross = (a, b) => [a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0]];
    const dot = (a, b) => a[0]*b[0] + a[1]*b[1] + a[2]*b[2];
    const normalize = (v) => { const len = Math.sqrt(v[0]*v[0]+v[1]*v[1]+v[2]*v[2]); return len < 1e-10 ? [0,0,1] : [v[0]/len, v[1]/len, v[2]/len]; };

    // ── Compute per-triangle normals ──
    const triNormals = new Array(triangleCount);
    for (let t = 0; t < triangleCount; t++) {
        const p0 = canonicalPositions[canonicalIdx[t * 3]];
        const p1 = canonicalPositions[canonicalIdx[t * 3 + 1]];
        const p2 = canonicalPositions[canonicalIdx[t * 3 + 2]];
        triNormals[t] = normalize(cross(sub(p1, p0), sub(p2, p0)));
    }

    // ── Vertex-level normal clustering: split adjacency at vertices where normals diverge ──
    const SPLIT_THRESHOLD = 30; // degrees
    const cosSplit = Math.cos(SPLIT_THRESHOLD * Math.PI / 180);
    const splitPairs = new Set(); // "minT,maxT" keys for pairs to split

    for (let vi = 0; vi < uniqueVerts; vi++) {
        const triArray = Array.from(vertToTris[vi]);
        if (triArray.length < 2) continue;

        // Cluster triangles at this vertex by normal direction
        // Greedy clustering: first triangle starts a cluster, subsequent triangles
        // join the nearest cluster if within threshold, otherwise start a new one.
        const clusters = []; // each cluster: [[tIdx, normal], ...]
        for (const t of triArray) {
            const n = triNormals[t];
            let bestCluster = -1, bestDot = -Infinity;
            for (let c = 0; c < clusters.length; c++) {
                // Use the first triangle's normal as cluster representative
                const repN = clusters[c][0][1];
                const d = dot(n, repN);
                if (d > bestDot) { bestDot = d; bestCluster = c; }
            }
            if (bestCluster >= 0 && bestDot >= cosSplit) {
                clusters[bestCluster].push([t, n]);
            } else {
                clusters.push([[t, n]]);
            }
        }

        // If multiple clusters exist at this vertex, split all cross-cluster pairs
        if (clusters.length > 1) {
            for (let ci = 0; ci < clusters.length; ci++) {
                for (let cj = ci + 1; cj < clusters.length; cj++) {
                    for (const [tA,] of clusters[ci]) {
                        for (const [tB,] of clusters[cj]) {
                            const key = tA < tB ? `${tA},${tB}` : `${tB},${tA}`;
                            splitPairs.add(key);
                        }
                    }
                }
            }
        }
    }

    console.log(`[three-module] Vertex normal clustering: ${splitPairs.size} split pairs at ${SPLIT_THRESHOLD}° threshold`);

    // ── Build adjacency with split pairs removed ──
    const adjacency = new Array(triangleCount);
    for (let i = 0; i < triangleCount; i++) adjacency[i] = new Set();

    for (let vi = 0; vi < uniqueVerts; vi++) {
        const triArray = Array.from(vertToTris[vi]);
        for (let i = 0; i < triArray.length; i++) {
            for (let j = i + 1; j < triArray.length; j++) {
                const tA = triArray[i], tB = triArray[j];
                const key = tA < tB ? `${tA},${tB}` : `${tB},${tA}`;
                if (splitPairs.has(key)) continue;
                adjacency[tA].add(tB);
                adjacency[tB].add(tA);
            }
        }
    }

    // ── Step 3: BFS connected components ──
    const visited = new Array(triangleCount).fill(false);
    const components = [];
    const minTri = 4;

    for (let start = 0; start < triangleCount; start++) {
        if (visited[start]) continue;

        const comp = [];
        const queue = [start];
        visited[start] = true;

        while (queue.length > 0) {
            const t = queue.pop();
            comp.push(t);
            for (const neighbor of adjacency[t]) {
                if (!visited[neighbor]) {
                    visited[neighbor] = true;
                    queue.push(neighbor);
                }
            }
        }

        if (comp.length >= minTri) {
            components.push(comp);
        }
    }

    // No qualified components → treat whole mesh as one
    if (components.length === 0) {
        return [{ triangles: [...Array(triangleCount).keys()], name: 'MainBody' }];
    }

    // Sort largest first
    components.sort((a, b) => b.length - a.length);

    const result = components.map((comp, i) => ({
        triangles: comp,
        name: components.length === 1 ? 'MainBody' : `Component_${i + 1}`,
        triangleCount: comp.length
    }));

    console.log(`[three-module] Component separation: ${result.length} component(s) — ${result.map(c => `${c.name}:${c.triangleCount}`).join(', ')}`);
    return result;
}

// ----------------------------------------------------------------
// initScene(containerId: string)
// ----------------------------------------------------------------
export function initScene(containerId) {
    console.log('[three-module] initScene called with containerId:', containerId);

    if (_scene && _currentContainerId && _currentContainerId !== containerId) {
        console.log('[three-module] Container changed, cleaning up old scene');
        if (_animationId) { cancelAnimationFrame(_animationId); _animationId = null; }
        if (_controls) { _controls.dispose(); _controls = null; }
        _composer = null;
        _bloomPass = null;
        if (_renderer) {
            _renderer.dispose();
            if (_renderer.domElement && _renderer.domElement.parentNode) {
                _renderer.domElement.parentNode.removeChild(_renderer.domElement);
            }
            _renderer = null;
        }
        _scene = null;
        _camera = null;
        _currentModel = null;
        _loadedUrl = null;
        _componentMap.clear();
        _selectedComponent = null;
        _containerElement = null;
        _frameCount = 0;
    }

    if (_scene) {
        console.log('[three-module] Scene already exists, reusing');
        return;
    }

    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`[three-module] Container element '${containerId}' not found.`);
        return;
    }

    const w = container.clientWidth || 800;
    const h = container.clientHeight || 600;
    _containerElement = container;

    // Scene
    _scene = new THREE.Scene();
    _scene.background = new THREE.Color(0x0a0e1a);
    _scene.fog = new THREE.Fog(0x0a0e1a, 5, 50);

    // Camera
    const aspect = w / h;
    _camera = new THREE.PerspectiveCamera(50, aspect, 0.1, 100);
    _camera.position.set(5, 3, 8);
    _camera.lookAt(0, 0, 0);

    // Renderer
    _renderer = new THREE.WebGLRenderer({ antialias: true });
    _renderer.setSize(w, h);
    _renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    _renderer.shadowMap.enabled = true;
    _renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    _renderer.toneMapping = THREE.ACESFilmicToneMapping;
    _renderer.toneMappingExposure = 1.2;
    _renderer.domElement.style.display = 'block';
    _renderer.domElement.style.width = '100%';
    _renderer.domElement.style.height = '100%';
    _renderer.domElement.style.position = 'absolute';
    _renderer.domElement.style.top = '0';
    _renderer.domElement.style.left = '0';
    _renderer.domElement.style.zIndex = '1';
    container.appendChild(_renderer.domElement);

    // Post-processing
    _composer = new EffectComposer(_renderer);
    const renderPass = new RenderPass(_scene, _camera);
    _composer.addPass(renderPass);
    _bloomPass = new UnrealBloomPass(
        new THREE.Vector2(container.clientWidth, container.clientHeight),
        0.15, 0.4, 0.85
    );
    _composer.addPass(_bloomPass);

    // Lighting
    const ambientLight = new THREE.AmbientLight(0xffffff, 2.0);
    _scene.add(ambientLight);
    const directionalLight = new THREE.DirectionalLight(0xffffff, 3.0);
    directionalLight.position.set(5, 10, 5);
    directionalLight.castShadow = true;
    directionalLight.shadow.mapSize.width = 1024;
    directionalLight.shadow.mapSize.height = 1024;
    directionalLight.shadow.camera.near = 0.5;
    directionalLight.shadow.camera.far = 500;
    _scene.add(directionalLight);
    const rimLight = new THREE.DirectionalLight(0x00d4ff, 1.5);
    rimLight.position.set(-3, 2, -3);
    _scene.add(rimLight);

    // Grid
    const gridHelper = new THREE.GridHelper(10, 20, 0x00d4ff, 0x00d4ff44);
    _scene.add(gridHelper);

    // Controls
    _controls = new OrbitControls(_camera, _renderer.domElement);
    _controls.enableDamping = true;
    _controls.dampingFactor = 0.08;
    _controls.minDistance = 1;
    _controls.maxDistance = 30;
    _controls.target.set(0, 0, 0);
    _controls.update();

    // Animation loop
    function animate() {
        _animationId = requestAnimationFrame(animate);
        if (_controls) _controls.update();
        if (_composer) _composer.render();
        _frameCount++;
    }
    animate();

    // Resize
    const resizeObserver = new ResizeObserver(() => {
        if (!_renderer || !_camera || !container) return;
        const rw = container.clientWidth;
        const rh = container.clientHeight;
        if (rw > 0 && rh > 0) {
            _renderer.setSize(rw, rh);
            _camera.aspect = rw / rh;
            _camera.updateProjectionMatrix();
            if (_composer) _composer.setSize(rw, rh);
        }
    });
    resizeObserver.observe(container);

    requestAnimationFrame(() => {
        if (_renderer && container) {
            const rw = container.clientWidth;
            const rh = container.clientHeight;
            if (rw > 0 && rh > 0) {
                _renderer.setSize(rw, rh);
                _camera.aspect = rw / rh;
                _camera.updateProjectionMatrix();
                if (_composer) _composer.setSize(rw, rh);
            }
        }
    });

    console.log('[three-module] Scene initialized successfully.');
    _currentContainerId = containerId;
}

// ----------------------------------------------------------------
// loadModel(glbUrl: string, force?: boolean)
// ----------------------------------------------------------------
export function loadModel(glbUrl, force) {
    return new Promise((resolve, reject) => {
        console.log('[three-module] loadModel called with URL:', glbUrl, 'force:', force);

        if (!_scene) {
            console.error('[three-module] loadModel: scene not initialized.');
            reject(new Error('Scene not initialized. Call initScene first.'));
            return;
        }

        if (!glbUrl || typeof glbUrl !== 'string' || !glbUrl.startsWith('/') || glbUrl.startsWith('_')) {
            console.warn('[three-module] Invalid model URL, skipping load:', glbUrl);
            resolve({ success: false, reason: 'invalid-url' });
            return;
        }

        if (!force && _loadedUrl === glbUrl) {
            console.log('[three-module] Model already loaded, skipping:', glbUrl);
            resolve({ success: true, reason: 'already-loaded' });
            return;
        }
        _loadedUrl = glbUrl;

        console.log('[three-module] Starting GLTF load from:', glbUrl);
        const loader = new GLTFLoader();
        loader.load(
            glbUrl,
            (gltf) => {
                console.log('[three-module] GLTF loaded successfully');

                let hasValidGeometry = false;
                gltf.scene.traverse((child) => {
                    if (child.isMesh && child.geometry) {
                        const pos = child.geometry.getAttribute('position');
                        if (pos && pos.count > 0) {
                            const x = pos.getX(0);
                            if (isFinite(x)) hasValidGeometry = true;
                        }
                    }
                });

                if (!hasValidGeometry) {
                    console.warn('[three-module] GLB has no valid geometry, skipping model load');
                    resolve({ success: false, reason: 'invalid-geometry' });
                    return;
                }

                if (_currentModel) {
                    _scene.remove(_currentModel);
                    _componentMap.clear();
                    _selectedComponent = null;
                }

                _currentModel = gltf.scene;
                _currentModel.traverse((child) => {
                    if (child.isMesh) {
                        child.castShadow = true;
                        child.receiveShadow = true;
                    }
                });

                _collectNamedObjects(_currentModel, _componentMap);
                _scene.add(_currentModel);

                _fitCameraToModel(_currentModel);
                resolve({ success: true, componentCount: _componentMap.size });
            },
            undefined,
            (error) => {
                console.error(`[three-module] Failed to load model: ${glbUrl}`, error);
                reject(error);
            }
        );
    });
}

// ----------------------------------------------------------------
// loadStlModel(stlUrl: string, triangleCounts?: number[])
// Load STL, separate components using exact triangle counts if provided,
// otherwise fall back to heuristic connectivity-based separation.
// ----------------------------------------------------------------
export function loadStlModel(stlUrl, triangleCounts) {
    return new Promise((resolve, reject) => {
        console.log('[three-module] loadStlModel called with URL:', stlUrl,
            triangleCounts ? `triangleCounts: [${triangleCounts}]` : '(no counts, using heuristic)');

        if (!_scene) {
            console.error('[three-module] loadStlModel: scene not initialized.');
            reject(new Error('Scene not initialized. Call initScene first.'));
            return;
        }

        if (!stlUrl || typeof stlUrl !== 'string') {
            console.warn('[three-module] Invalid STL URL, skipping load:', stlUrl);
            resolve({ success: false, reason: 'invalid-url' });
            return;
        }

        console.log('[three-module] Starting STL load from:', stlUrl);
        const loader = new STLLoader();

        loader.load(
            stlUrl,
            (geometry) => {
                console.log('[three-module] STL loaded successfully');

                const pos = geometry.getAttribute('position');
                if (!pos || pos.count === 0) {
                    console.warn('[three-module] STL has no valid geometry, skipping');
                    resolve({ success: false, reason: 'invalid-geometry' });
                    return;
                }

                // Remove previous model
                if (_currentModel) {
                    _scene.remove(_currentModel);
                    _componentMap.clear();
                    _selectedComponent = null;
                }

                // Separate into components: use exact counts if provided, fallback to heuristic
                const componentInfos = (triangleCounts && Array.isArray(triangleCounts) && triangleCounts.length > 0)
                    ? _splitByTriangleCounts(pos, triangleCounts)
                    : _separateStlComponents(geometry);

                const componentList = [];

                _currentModel = new THREE.Group();
                _currentModel.name = 'STLModel';

                componentInfos.forEach((compInfo, index) => {
                    // Create a new BufferGeometry for this component's triangles
                    const compGeo = new THREE.BufferGeometry();
                    const compPositions = new Float32Array(compInfo.triangles.length * 9);

                    compInfo.triangles.forEach((triIdx, i) => {
                        const srcIdx = triIdx * 9;
                        const dstIdx = i * 9;
                        compPositions[dstIdx] = pos.getX(triIdx * 3);
                        compPositions[dstIdx + 1] = pos.getY(triIdx * 3);
                        compPositions[dstIdx + 2] = pos.getZ(triIdx * 3);
                        compPositions[dstIdx + 3] = pos.getX(triIdx * 3 + 1);
                        compPositions[dstIdx + 4] = pos.getY(triIdx * 3 + 1);
                        compPositions[dstIdx + 5] = pos.getZ(triIdx * 3 + 1);
                        compPositions[dstIdx + 6] = pos.getX(triIdx * 3 + 2);
                        compPositions[dstIdx + 7] = pos.getY(triIdx * 3 + 2);
                        compPositions[dstIdx + 8] = pos.getZ(triIdx * 3 + 2);
                    });

                    compGeo.setAttribute('position', new THREE.BufferAttribute(compPositions, 3));
                    compGeo.computeVertexNormals();

                    // Assign color from palette
                    const colorIdx = index % _COMPONENT_COLORS.length;
                    const color = _COMPONENT_COLORS[colorIdx];

                    const material = new THREE.MeshPhongMaterial({
                        color: color,
                        specular: 0x111111,
                        shininess: 200,
                        side: THREE.DoubleSide
                    });

                    const compMesh = new THREE.Mesh(compGeo, material);
                    compMesh.name = compInfo.name;
                    compMesh.castShadow = true;
                    compMesh.receiveShadow = true;
                    compMesh.userData = {
                        componentName: compInfo.name,
                        triangleCount: compInfo.triangles.length,
                        originalColor: color,
                        isStlComponent: true
                    };

                    _currentModel.add(compMesh);
                    _componentMap.set(compInfo.name, compMesh);

                    // Compute bounding box for this component
                    const box = new THREE.Box3().setFromObject(compMesh);
                    const center = box.getCenter(new THREE.Vector3());

                    componentList.push({
                        name: compInfo.name,
                        triangleCount: compInfo.triangles.length,
                        center: center.toArray(),
                        minBound: box.min.toArray(),
                        maxBound: box.max.toArray(),
                        color: '#' + color.toString(16).padStart(6, '0')
                    });
                });

                _scene.add(_currentModel);

                // Fit camera
                _fitCameraToModel(_currentModel);

                console.log(`[three-module] STL loaded with ${componentList.length} components`);
                resolve({
                    success: true,
                    componentCount: componentList.length,
                    components: componentList,
                    totalTriangles: pos.count / 3
                });
            },
            (progress) => {
                if (progress.total > 0) {
                    console.log(`[three-module] STL loading: ${(progress.loaded / progress.total * 100).toFixed(2)}%`);
                }
            },
            (error) => {
                console.error(`[three-module] Failed to load STL: ${stlUrl}`, error);
                reject(error);
            }
        );
    });
}

// ----------------------------------------------------------------
// getComponentList() → JSON array of component metadata
// ----------------------------------------------------------------
export function getComponentList() {
    if (!_currentModel || _componentMap.size === 0) {
        console.log(`[three-module] getComponentList: _currentModel=${!!_currentModel}, _componentMap.size=${_componentMap.size}`);
        return JSON.stringify([]);
    }

    const result = [];
    _componentMap.forEach((obj, name) => {
        const box = new THREE.Box3().setFromObject(obj);
        const center = box.getCenter(new THREE.Vector3());
        const size = box.getSize(new THREE.Vector3());

        let visible = true;
        obj.traverse((child) => {
            if (child.isMesh && !child.visible) visible = false;
        });

        result.push({
            name: name,
            visible: visible,
            center: center.toArray(),
            size: size.toArray(),
            triangleCount: obj.userData?.triangleCount || 0,
            color: obj.userData?.originalColor
                ? '#' + obj.userData.originalColor.toString(16).padStart(6, '0')
                : '#00d4ff',
            isSelected: obj === _selectedComponent
        });
    });

    console.log(`[three-module] getComponentList: returning ${result.length} components`);
    return JSON.stringify(result);
}

// ----------------------------------------------------------------
// selectComponent(componentName: string) — persistent selection highlight
// ----------------------------------------------------------------
export function selectComponent(componentName) {
    if (!_scene) return;

    // Clear previous selection
    deselectAllComponents();

    const obj = _componentMap.get(componentName) || _findObjectByName(_currentModel || _scene, componentName);
    if (!obj) {
        console.warn(`[three-module] Component '${componentName}' not found for selection.`);
        return;
    }

    _selectedComponent = obj;

    obj.traverse((child) => {
        if (child.isMesh && child.material) {
            const materials = Array.isArray(child.material) ? child.material : [child.material];
            for (const mat of materials) {
                mat.emissive = new THREE.Color(0xffd700);
                mat.emissiveIntensity = 0.4;
                mat.needsUpdate = true;
            }
        }
    });

    console.log(`[three-module] Selected component: ${componentName}`);
}

// ----------------------------------------------------------------
// deselectAllComponents() — clear all persistent selection highlights
// ----------------------------------------------------------------
export function deselectAllComponents() {
    if (!_selectedComponent) return;

    _selectedComponent.traverse((child) => {
        if (child.isMesh && child.material) {
            const materials = Array.isArray(child.material) ? child.material : [child.material];
            for (const mat of materials) {
                mat.emissiveIntensity = 0;
                mat.needsUpdate = true;
            }
        }
    });

    _selectedComponent = null;
}

// ----------------------------------------------------------------
// toggleComponentVisibility(componentName: string, visible: boolean)
// ----------------------------------------------------------------
export function toggleComponentVisibility(componentName, visible) {
    if (!_scene) return;

    const obj = _componentMap.get(componentName) || _findObjectByName(_currentModel || _scene, componentName);
    if (!obj) {
        console.warn(`[three-module] Component '${componentName}' not found for visibility toggle.`);
        return;
    }

    obj.traverse((child) => {
        if (child.isMesh) {
            child.visible = visible;
        }
    });

    console.log(`[three-module] Component '${componentName}' visibility: ${visible}`);
}

// ----------------------------------------------------------------
// setComponentColor(componentName: string, colorHex: string)
// ----------------------------------------------------------------
export function setComponentColor(componentName, colorHex) {
    if (!_scene) return;

    const obj = _componentMap.get(componentName) || _findObjectByName(_currentModel || _scene, componentName);
    if (!obj) {
        console.warn(`[three-module] Component '${componentName}' not found for color change.`);
        return;
    }

    const color = new THREE.Color(colorHex);
    obj.traverse((child) => {
        if (child.isMesh && child.material) {
            const materials = Array.isArray(child.material) ? child.material : [child.material];
            for (const mat of materials) {
                if (mat.color) {
                    mat.color.copy(color);
                    mat.needsUpdate = true;
                }
            }
        }
    });

    // Track custom color
    if (obj.userData) {
        obj.userData.customColor = colorHex;
    }
}

// ----------------------------------------------------------------
// setComponentScale(componentName: string, scale: number)
// ----------------------------------------------------------------
export function setComponentScale(componentName, scale) {
    if (!_scene) return;

    const obj = _componentMap.get(componentName) || _findObjectByName(_currentModel || _scene, componentName);
    if (!obj) {
        console.warn(`[three-module] Component '${componentName}' not found for scale change.`);
        return;
    }

    const s = parseFloat(scale);
    if (isNaN(s) || s <= 0) return;

    obj.scale.set(s, s, s);
    console.log(`[three-module] Component '${componentName}' scaled to ${s}x`);
}

// ----------------------------------------------------------------
// showAllComponents() — make all components visible
// ----------------------------------------------------------------
export function showAllComponents() {
    if (!_scene || _componentMap.size === 0) return;

    _componentMap.forEach((obj) => {
        obj.traverse((child) => {
            if (child.isMesh) {
                child.visible = true;
            }
        });
    });

    console.log('[three-module] All components set to visible');
}

// ----------------------------------------------------------------
// highlightComponent(componentName: string) — temporary flash highlight
// ----------------------------------------------------------------
export function highlightComponent(componentName) {
    if (!_scene) return;

    const obj = _componentMap.get(componentName) || _findObjectByName(_currentModel || _scene, componentName);
    if (!obj) return;

    // Save current selection state
    const wasSelected = obj === _selectedComponent;

    obj.traverse((child) => {
        if (child.isMesh && child.material) {
            child.material.emissive = new THREE.Color(0x00d4ff);
            child.material.emissiveIntensity = 0.3;
            child.material.needsUpdate = true;
        }
    });

    if (_highlightTimeout) clearTimeout(_highlightTimeout);
    _highlightTimeout = setTimeout(() => {
        obj.traverse((child) => {
            if (child.isMesh && child.material) {
                if (wasSelected && obj === _selectedComponent) {
                    child.material.emissive = new THREE.Color(0xffd700);
                    child.material.emissiveIntensity = 0.4;
                } else {
                    child.material.emissiveIntensity = 0;
                }
                child.material.needsUpdate = true;
            }
        });
        _highlightTimeout = null;
    }, 800);
}

// ----------------------------------------------------------------
// updateComponentColor(componentName: string, colorHex: string)
// Legacy alias for setComponentColor
// ----------------------------------------------------------------
export function updateComponentColor(componentName, colorHex) {
    setComponentColor(componentName, colorHex);
}

// ----------------------------------------------------------------
// buildFromSceneDescription(sceneJson: string)
// ----------------------------------------------------------------
export function buildFromSceneDescription(sceneJson) {
    console.log('[three-module] buildFromSceneDescription called, _scene exists:', !!_scene);
    if (!_scene) return;

    let scene;
    try {
        scene = JSON.parse(sceneJson);
    } catch (e) {
        console.error('[three-module] Failed to parse scene JSON:', e);
        return;
    }

    console.log('[three-module] Parsed scene:', JSON.stringify(scene).substring(0, 200));

    if (_currentModel) {
        _scene.remove(_currentModel);
        _currentModel = null;
        _componentMap.clear();
        _selectedComponent = null;
    }

    const group = new THREE.Group();
    const objects = scene.objects || scene.Objects || [];
    console.log('[three-module] Objects count:', objects.length);

    for (const obj of objects) {
        let geometry;
        const type = (obj.type || obj.Type || 'box').toLowerCase();
        const size = obj.size || obj.Size || [1, 1, 1];

        if (type === 'sphere') {
            geometry = new THREE.SphereGeometry(size[0] || 0.5, size[1] || 32, size[2] || 32);
        } else if (type === 'cylinder') {
            geometry = new THREE.CylinderGeometry(size[0] || 0.5, size[0] || 0.5, size[1] || 2, size[2] || 32);
        } else if (type === 'cone') {
            geometry = new THREE.ConeGeometry(size[0] || 0.5, size[1] || 2, size[2] || 32);
        } else {
            geometry = new THREE.BoxGeometry(size[0] || 1, size[1] || 1, size[2] || 1);
        }

        const colorVal = obj.color || obj.Color || '#00d4ff';
        const color = new THREE.Color(colorVal);
        const matProps = obj.material || obj.Material || {};
        const metalness = matProps.metalness ?? matProps.Metalness ?? 0.5;
        const roughness = matProps.roughness ?? matProps.Roughness ?? 0.3;
        const material = new THREE.MeshStandardMaterial({ color, metalness, roughness });

        const mesh = new THREE.Mesh(geometry, material);
        mesh.name = type;
        const pos = obj.position || obj.Position || [0, 0, 0];
        mesh.position.set(pos[0] || 0, pos[1] || 0, pos[2] || 0);
        if (obj.rotation || obj.Rotation) {
            const rot = obj.rotation || obj.Rotation;
            mesh.rotation.set(rot[0] || 0, rot[1] || 0, rot[2] || 0);
        }
        mesh.castShadow = true;
        mesh.receiveShadow = true;

        group.add(mesh);
        _componentMap.set(mesh.name, mesh);
    }

    _currentModel = group;
    _scene.add(group);
    _fitCameraToModel(group);
}

// ----------------------------------------------------------------
// Internal: Fit camera to model
// ----------------------------------------------------------------
function _fitCameraToModel(model) {
    try {
        const box = new THREE.Box3().setFromObject(model, true);
        if (!box.isEmpty() && isFinite(box.min.x)) {
            const center = box.getCenter(new THREE.Vector3());
            const size = box.getSize(new THREE.Vector3());
            const maxDim = Math.max(size.x, size.y, size.z);
            const dist = Math.max(maxDim * 2.5, 3);
            _camera.position.set(center.x + dist * 0.5, center.y + dist * 0.4, center.z + dist);
            _camera.lookAt(center);
            _controls.target.copy(center);
            _controls.update();
        }
    } catch (e) {
        console.warn('[three-module] Camera fit failed:', e);
    }
}

// ----------------------------------------------------------------
// getSceneSnapshot()
// ----------------------------------------------------------------
export function getSceneSnapshot() {
    if (!_camera) return null;
    return {
        camera: {
            position: _camera.position.toArray(),
            rotation: [_camera.rotation.x, _camera.rotation.y, _camera.rotation.z]
        },
        target: _controls?.target?.toArray() || [0, 0, 0]
    };
}

// ----------------------------------------------------------------
// setBloomEnabled(enabled: boolean)
// ----------------------------------------------------------------
export function setBloomEnabled(enabled) {
    if (_bloomPass) {
        _bloomPass.strength = enabled ? 0.15 : 0;
    }
}

// ----------------------------------------------------------------
// dispose()
// ----------------------------------------------------------------
export function dispose() {
    if (_highlightTimeout) {
        clearTimeout(_highlightTimeout);
        _highlightTimeout = null;
    }

    if (_animationId) {
        cancelAnimationFrame(_animationId);
        _animationId = null;
    }

    if (_controls) {
        _controls.dispose();
        _controls = null;
    }

    _loadedUrl = null;
    _componentMap.clear();
    _selectedComponent = null;

    if (_renderer) {
        _renderer.dispose();
        if (_containerElement && _renderer.domElement) {
            _containerElement.removeChild(_renderer.domElement);
        }
        _renderer = null;
    }

    _scene = null;
    _camera = null;
    _containerElement = null;
    _frameCount = 0;
    _currentContainerId = null;
    _composer = null;
    _bloomPass = null;
    _currentModel = null;

    console.log('[three-module] Disposed.');
}

// ----------------------------------------------------------------
// resetLoadedUrl()
// ----------------------------------------------------------------
export function resetLoadedUrl() {
    _loadedUrl = null;
    console.log('[three-module] Reset loaded URL cache.');
}

// ----------------------------------------------------------------
// Expose all public functions to window for Blazor IJSRuntime access
// Used by DesignStudio and other parent components that call
// JS.InvokeVoidAsync / JS.InvokeAsync directly without module ref.
// ----------------------------------------------------------------
(function _exposeAllToWindow() {
    // Check if we're the first import — avoid double-exposing when
    // the module is imported multiple times (ModelViewer calls import()
    // on every page render, but the browser caches the module).
    if (window.__threeModuleExposed) return;
    window.__threeModuleExposed = true;

    window.initScene = initScene;
    window.loadModel = loadModel;
    window.loadStlModel = loadStlModel;
    window.getComponentList = getComponentList;
    window.selectComponent = selectComponent;
    window.deselectAllComponents = deselectAllComponents;
    window.toggleComponentVisibility = toggleComponentVisibility;
    window.setComponentColor = setComponentColor;
    window.setComponentScale = setComponentScale;
    window.showAllComponents = showAllComponents;
    window.highlightComponent = highlightComponent;
    window.updateComponentColor = updateComponentColor;
    window.buildFromSceneDescription = buildFromSceneDescription;
    window.getSceneSnapshot = getSceneSnapshot;
    window.setBloomEnabled = setBloomEnabled;
    window.dispose = dispose;
    window.resetLoadedUrl = resetLoadedUrl;

    console.log('[three-module] All functions exposed to window for IJSRuntime.');
})();
