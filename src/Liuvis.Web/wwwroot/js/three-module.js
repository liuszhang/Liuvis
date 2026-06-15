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
 *   loadModel(glbUrl)                  — Load a GLB model into the scene
 *   updateComponentColor(componentId, color) — Change a component's material color
 *   highlightComponent(componentId)    — Highlight a component with emissive glow
 *   setBloomEnabled(enabled)           — Toggle bloom post-processing
 *   getSceneSnapshot()                 — Return current camera position/rotation as JSON
 *   dispose()                          — Clean up all resources
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
let _loadedUrl = null;  // track last loaded URL to prevent redundant reloads
let _animationId = null;
let _componentMap = new Map(); // componentName → THREE.Object3D
let _containerElement = null;
let _frameCount = 0;

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
    if (_scene) {
        console.warn('[three-module] Scene already initialized.');
        return;
    }

    const container = document.getElementById(containerId);
    if (!container) {
        console.error(`[three-module] Container element '${containerId}' not found.`);
        // List all elements with ids for debugging
        const allWithIds = document.querySelectorAll('[id]');
        console.log('[three-module] Elements with IDs on page:', Array.from(allWithIds).map(e => '#' + e.id));
        return;
    }
    console.log('[three-module] Container found. Size:', container.clientWidth, 'x', container.clientHeight);
    _containerElement = container;

    // Scene
    _scene = new THREE.Scene();
    _scene.background = new THREE.Color(0x0a0e1a);
    _scene.fog = new THREE.Fog(0x0a0e1a, 5, 50);

    // Camera
    const aspect = container.clientWidth / (container.clientHeight || 1);
    _camera = new THREE.PerspectiveCamera(50, aspect, 0.1, 100);
    _camera.position.set(5, 3, 8);
    _camera.lookAt(0, 0, 0);

    // Renderer — opaque background, scene.background handles the color
    _renderer = new THREE.WebGLRenderer({ antialias: true });
    _renderer.setSize(container.clientWidth, container.clientHeight);
    _renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    _renderer.shadowMap.enabled = true;
    _renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    _renderer.toneMapping = THREE.ACESFilmicToneMapping;
    _renderer.toneMappingExposure = 1.2;
    // Ensure canvas is visible regardless of inherited CSS
    _renderer.domElement.style.display = 'block';
    _renderer.domElement.style.width = '100%';
    _renderer.domElement.style.height = '100%';
    container.appendChild(_renderer.domElement);
    console.log('[three-module] Canvas appended. Canvas size:', _renderer.domElement.width, 'x', _renderer.domElement.height,
        '| computed:', getComputedStyle(_renderer.domElement).width, 'x', getComputedStyle(_renderer.domElement).height);

    // Post-processing
    _composer = new EffectComposer(_renderer);
    const renderPass = new RenderPass(_scene, _camera);
    _composer.addPass(renderPass);

    _bloomPass = new UnrealBloomPass(
        new THREE.Vector2(container.clientWidth, container.clientHeight),
        0.15,  // strength (reduced from 0.5 to avoid washing out)
        0.4,   // radius
        0.85   // threshold (most colors below this won't bloom)
    );
    _composer.addPass(_bloomPass);

    // Lighting (stronger ambient ensures models are always visible)
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

    // Grid helper (holographic style)
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
        if (_frameCount === 1) {
            console.log('[three-module] First frame rendered! Scene children:', _scene.children.length);
        }
        if (_frameCount % 60 === 0) {
            console.log('[three-module] Frame', _frameCount, '| scene children:', _scene.children.length, '| canvas visible:', _renderer.domElement.offsetParent !== null);
        }
    }
    animate();

    // Resize handler
    const resizeObserver = new ResizeObserver(() => {
        if (!_renderer || !_camera || !container) return;
        const w = container.clientWidth;
        const h = container.clientHeight;
        _renderer.setSize(w, h);
        _camera.aspect = w / (h || 1);
        _camera.updateProjectionMatrix();
        if (_composer) _composer.setSize(w, h);
    });
    resizeObserver.observe(container);

    console.log('[three-module] Scene initialized successfully.');
}

// ----------------------------------------------------------------
// loadModel(glbUrl: string)
// ----------------------------------------------------------------
export function loadModel(glbUrl) {
    return new Promise((resolve, reject) => {
        console.log('[three-module] loadModel called with URL:', glbUrl);

        if (!_scene) {
            console.error('[three-module] loadModel: scene not initialized.');
            reject(new Error('Scene not initialized. Call initScene first.'));
            return;
        }

        // Validate URL
        if (!glbUrl || typeof glbUrl !== 'string' || !glbUrl.startsWith('/') || glbUrl.startsWith('_')) {
            console.warn('[three-module] Invalid model URL, skipping load:', glbUrl);
            resolve({ success: false, reason: 'invalid-url' });
            return;
        }

        // Skip if already loaded (prevents flash from redundant reloads)
        if (_loadedUrl === glbUrl) {
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
                console.log('[three-module] GLTF loaded successfully, adding to scene');
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

                // Build component name map for later lookups
                _collectNamedObjects(_currentModel, _componentMap);

                _scene.add(_currentModel);

                // Fit camera to model bounds
                const box = new THREE.Box3().setFromObject(_currentModel);
                const center = box.getCenter(new THREE.Vector3());
                const size = box.getSize(new THREE.Vector3());
                const maxDim = Math.max(size.x, size.y, size.z);
                const dist = Math.max(maxDim * 2.5, 10);  // ensure minimum distance
                _camera.position.set(center.x + dist * 0.5, center.y + dist * 0.4, center.z + dist);
                _camera.lookAt(center);
                _controls.target.copy(center);
                _controls.update();

                console.log(`[three-module] Model loaded: ${glbUrl}`);
                console.log('[three-module] Model bounding box min/max/center/size/maxDim:', 
                    box.min.toArray(), box.max.toArray(), center.toArray(), size.toArray(), maxDim);
                console.log('[three-module] Camera new position:', _camera.position.toArray(), 'target:', _controls.target.toArray());
                resolve({ success: true, componentCount: _componentMap.size });
            },
            (progress) => {
                // Optional: report progress
            },
            (error) => {
                console.error(`[three-module] Failed to load model: ${glbUrl}`, error);
                console.error('[three-module] Error details:', error.message || error);
                reject(error);
            }
        );
    });
}

// ----------------------------------------------------------------
// updateComponentColor(componentName: string, colorHex: string)
// ----------------------------------------------------------------
export function updateComponentColor(componentName, colorHex) {
    if (!_scene) {
        console.error('[three-module] Scene not initialized.');
        return;
    }

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

    console.log(`[three-module] Component '${componentName}' color updated to ${colorHex}`);
}

// ----------------------------------------------------------------
// highlightComponent(componentName: string)
// ----------------------------------------------------------------
export function highlightComponent(componentName) {
    if (!_scene) {
        console.error('[three-module] Scene not initialized.');
        return;
    }

    // Reset all highlights
    if (_currentModel) {
        _currentModel.traverse((child) => {
            if (child.isMesh && child.material) {
                const materials = Array.isArray(child.material)
                    ? child.material
                    : [child.material];
                for (const mat of materials) {
                    if (mat.emissive && mat.userData._originalEmissive === undefined) {
                        mat.userData._originalEmissive = mat.emissive.getHex();
                    }
                    if (mat.emissive) {
                        mat.emissive.setHex(mat.userData._originalEmissive || 0x000000);
                    }
                    mat.needsUpdate = true;
                }
            }
        });
    }

    if (!componentName) return;

    let obj = _componentMap.get(componentName) ||
        _findObjectByName(_currentModel || _scene, componentName);

    if (!obj) {
        console.warn(`[three-module] Component '${componentName}' not found for highlight.`);
        return;
    }

    obj.traverse((child) => {
        if (child.isMesh && child.material) {
            const materials = Array.isArray(child.material)
                ? child.material
                : [child.material];
            for (const mat of materials) {
                if (mat.emissive) {
                    if (mat.userData._originalEmissive === undefined) {
                        mat.userData._originalEmissive = mat.emissive.getHex();
                    }
                    mat.emissive.setHex(0x00d4ff);
                    mat.emissiveIntensity = 0.8;
                }
                mat.needsUpdate = true;
            }
        }
    });

    console.log(`[three-module] Component '${componentName}' highlighted.`);
}

// ----------------------------------------------------------------
// setBloomEnabled(enabled: boolean)
// ----------------------------------------------------------------
export function setBloomEnabled(enabled) {
    if (_bloomPass) {
        _bloomPass.strength = enabled ? 0.5 : 0.0;
        console.log(`[three-module] Bloom ${enabled ? 'enabled' : 'disabled'}.`);
    }
}

// ----------------------------------------------------------------
// getSceneSnapshot(): string (JSON)
// ----------------------------------------------------------------
export function getSceneSnapshot() {
    if (!_camera || !_controls) {
        return JSON.stringify({ error: 'Scene not initialized' });
    }

    const snapshot = {
        cameraPosition: {
            x: _camera.position.x,
            y: _camera.position.y,
            z: _camera.position.z,
        },
        cameraRotation: {
            x: _camera.rotation.x,
            y: _camera.rotation.y,
            z: _camera.rotation.z,
        },
        target: {
            x: _controls.target.x,
            y: _controls.target.y,
            z: _controls.target.z,
        },
        componentCount: _componentMap.size,
        timestamp: new Date().toISOString(),
    };

    return JSON.stringify(snapshot);
}

// ----------------------------------------------------------------
// diagnose() — dump full state for debugging
// ----------------------------------------------------------------
export function diagnose() {
    const info = {
        sceneExists: !!_scene,
        rendererExists: !!_renderer,
        composerExists: !!_composer,
        cameraExists: !!_camera,
        animationRunning: !!_animationId,
        frameCount: _frameCount,
        sceneChildren: _scene ? _scene.children.length : -1,
        currentModel: !!_currentModel,
        loadedUrl: _loadedUrl,
        containerId: _containerElement ? _containerElement.id : 'none',
        containerSize: _containerElement
            ? `${_containerElement.clientWidth}x${_containerElement.clientHeight}`
            : 'N/A',
        canvasExists: !!(_renderer && _renderer.domElement),
        canvasSize: _renderer
            ? `${_renderer.domElement.width}x${_renderer.domElement.height}`
            : 'N/A',
        canvasVisible: _renderer ? (_renderer.domElement.offsetParent !== null) : false,
        canvasDisplay: _renderer ? getComputedStyle(_renderer.domElement).display : 'N/A',
        canvasVisibility: _renderer ? getComputedStyle(_renderer.domElement).visibility : 'N/A',
    };
    console.log('[three-module] DIAGNOSE:', JSON.stringify(info, null, 2));
    return JSON.stringify(info);
}

// ----------------------------------------------------------------
// dispose()
// ----------------------------------------------------------------
export function dispose() {
    if (_animationId) {
        cancelAnimationFrame(_animationId);
        _animationId = null;
    }

    if (_controls) {
        _controls.dispose();
        _controls = null;
    }

    if (_currentModel) {
        _currentModel.traverse((child) => {
            if (child.geometry) child.geometry.dispose();
            if (child.material) {
                const materials = Array.isArray(child.material)
                    ? child.material
                    : [child.material];
                for (const mat of materials) {
                    if (mat.map) mat.map.dispose();
                    if (mat.normalMap) mat.normalMap.dispose();
                    mat.dispose();
                }
            }
        });
        if (_scene) _scene.remove(_currentModel);
        _currentModel = null;
    }
    _loadedUrl = null;

    _componentMap.clear();

    if (_composer) {
        _composer = null;
    }
    _bloomPass = null;

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

    console.log('[three-module] Disposed.');
}
