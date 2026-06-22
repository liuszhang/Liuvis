/**
 * three-module.js — Three.js ES Module for Liuvis
 *
 * All JSInterop functions that Blazor calls via IJSRuntime.InvokeVoidAsync / InvokeAsync.
 * Three.js interaction (OrbitControls, animation loop) runs entirely in-browser.
 *
 * Uses ES import maps (configured in index.html / App.razor) to load Three.js from CDN.
 *
 * Exports:
 *   initScene(containerId)             — Initialize Three.js scene in a div
 *   loadModel(glbUrl, force)           — Load a GLB model into the scene
 *   updateComponentColor(componentId, color) — Change a component's material color
 *   highlightComponent(componentId)    — Highlight a component with emissive glow
 *   getSceneSnapshot()                 — Return current camera position/rotation as JSON
 *   dispose()                          — Clean up all resources
 *   resetLoadedUrl()                   — Clear URL cache to force reload
 */

// ----------------------------------------------------------------
// Three.js imports (resolved via import map in index.html)
// ----------------------------------------------------------------
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
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
// initScene(containerId: string)
// ----------------------------------------------------------------
export function initScene(containerId) {
    console.log('[three-module] initScene called with containerId:', containerId);

    // If scene exists but container ID changed, clean up old scene
    if (_scene && _currentContainerId && _currentContainerId !== containerId) {
        console.log('[three-module] Container changed, cleaning up old scene');
        if (_animationId) { cancelAnimationFrame(_animationId); _animationId = null; }
        if (_controls) { _controls.dispose(); _controls = null; }
        _composer = null;
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
    console.log('[three-module] Container found. Size:', w, 'x', h);

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

    // Grid helper
    const gridHelper = new THREE.GridHelper(10, 20, 0x00d4ff, 0x00d4ff44);
    _scene.add(gridHelper);

    // OrbitControls
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

    // Resize handler
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

    // Force initial resize after layout settles
    requestAnimationFrame(() => {
        if (_renderer && container) {
            const rw = container.clientWidth;
            const rh = container.clientHeight;
            if (rw > 0 && rh > 0) {
                _renderer.setSize(rw, rh);
                _camera.aspect = rw / rh;
                _camera.updateProjectionMatrix();
                if (_composer) _composer.setSize(rw, rh);
                console.log('[three-module] Post-layout resize:', rw, 'x', rh);
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

                // Validate geometry BEFORE adding to scene (NaN would corrupt everything)
                let hasValidGeometry = false;
                gltf.scene.traverse((child) => {
                    if (child.isMesh && child.geometry) {
                        const pos = child.geometry.getAttribute('position');
                        if (pos && pos.count > 0) {
                            const x = pos.getX(0);
                            if (isFinite(x)) {
                                hasValidGeometry = true;
                            }
                        }
                    }
                });

                if (!hasValidGeometry) {
                    console.warn('[three-module] GLB has no valid geometry, skipping model load');
                    resolve({ success: false, reason: 'invalid-geometry' });
                    return;
                }

                // Remove previous model
                if (_currentModel) {
                    _scene.remove(_currentModel);
                    _componentMap.clear();
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

                // Fit camera to model
                try {
                    const box = new THREE.Box3().setFromObject(_currentModel, true);
                    console.log('[three-module] Model box:', box.min.toArray(), box.max.toArray());
                    if (!box.isEmpty() && isFinite(box.min.x)) {
                        const center = box.getCenter(new THREE.Vector3());
                        const size = box.getSize(new THREE.Vector3());
                        const maxDim = Math.max(size.x, size.y, size.z);
                        const dist = Math.max(maxDim * 2.5, 3);
                        _camera.position.set(center.x + dist * 0.5, center.y + dist * 0.4, center.z + dist);
                        _camera.lookAt(center);
                        _controls.target.copy(center);
                        _controls.update();
                        console.log('[three-module] Camera positioned at:', _camera.position.toArray());
                    }
                } catch (e) {
                    console.warn('[three-module] Camera fit failed:', e);
                }

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
// updateComponentColor(componentName: string, colorHex: string)
// ----------------------------------------------------------------
export function updateComponentColor(componentName, colorHex) {
    if (!_scene) return;

    let obj = _componentMap.get(componentName);
    if (!obj) {
        const found = _findObjectByName(_currentModel || _scene, componentName);
        if (!found) {
            console.warn(`[three-module] Component '${componentName}' not found.`);
            return;
        }
        obj = found;
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
}

// ----------------------------------------------------------------
// highlightComponent(componentName: string)
// ----------------------------------------------------------------
export function highlightComponent(componentName) {
    if (!_scene) return;

    const obj = _componentMap.get(componentName) || _findObjectByName(_currentModel || _scene, componentName);
    if (!obj) return;

    obj.traverse((child) => {
        if (child.isMesh && child.material) {
            child.material.emissive = new THREE.Color(0x00d4ff);
            child.material.emissiveIntensity = 0.3;
            child.material.needsUpdate = true;
        }
    });

    setTimeout(() => {
        obj.traverse((child) => {
            if (child.isMesh && child.material) {
                child.material.emissiveIntensity = 0;
                child.material.needsUpdate = true;
            }
        });
    }, 800);
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
        console.log('[three-module] Creating mesh:', type, 'color:', colorVal, 'size:', size);
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
    console.log('[three-module] Group added. Children:', group.children.length, 'Scene children:', _scene.children.length);

    try {
        const box = new THREE.Box3().setFromObject(group, true);
        console.log('[three-module] Box:', JSON.stringify(box.min.toArray()), JSON.stringify(box.max.toArray()), 'empty:', box.isEmpty());
        if (!box.isEmpty() && isFinite(box.min.x)) {
            const center = box.getCenter(new THREE.Vector3());
            const size = box.getSize(new THREE.Vector3());
            const maxDim = Math.max(size.x, size.y, size.z);
            const dist = Math.max(maxDim * 2.5, 10);
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
    _refCount--;
    console.log('[three-module] dispose called. refCount:', _refCount);

    // Only actually dispose when no one is using the scene
    if (_refCount > 0) {
        console.log('[three-module] Scene still in use, skipping dispose.');
        return;
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
