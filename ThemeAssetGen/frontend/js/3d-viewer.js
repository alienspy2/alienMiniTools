// Three.js 3D 뷰어

class Viewer3D {
    constructor(containerId) {
        this.container = document.getElementById(containerId);
        this.scene = null;
        this.camera = null;
        this.renderer = null;
        this.controls = null;
        this.currentModel = null;
        this.animationId = null;

        this.init();
    }

    init() {
        // Scene
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0x1a1a2e);

        // Camera
        const width = this.container.clientWidth;
        const height = this.container.clientHeight;
        this.camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 1000);
        this.camera.position.set(3, 2, 3);

        // Renderer
        this.renderer = new THREE.WebGLRenderer({ antialias: true });
        this.renderer.setSize(width, height);
        this.renderer.setPixelRatio(window.devicePixelRatio);
        this.renderer.outputEncoding = THREE.sRGBEncoding;
        this.container.appendChild(this.renderer.domElement);

        // Controls
        this.controls = new THREE.OrbitControls(this.camera, this.renderer.domElement);
        this.controls.enableDamping = true;
        this.controls.dampingFactor = 0.05;

        // Lights
        const ambientLight = new THREE.AmbientLight(0xffffff, 0.5);
        this.scene.add(ambientLight);

        const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8);
        directionalLight.position.set(5, 10, 7);
        this.scene.add(directionalLight);

        const backLight = new THREE.DirectionalLight(0xffffff, 0.3);
        backLight.position.set(-5, 5, -5);
        this.scene.add(backLight);

        // Grid Helper
        const gridHelper = new THREE.GridHelper(10, 10, 0x444444, 0x333333);
        this.scene.add(gridHelper);

        // Resize handler
        window.addEventListener('resize', () => this.onResize());

        // Start animation
        this.animate();
    }

    animate() {
        this.animationId = requestAnimationFrame(() => this.animate());
        this.controls.update();
        this.renderer.render(this.scene, this.camera);
    }

    onResize() {
        const width = this.container.clientWidth;
        const height = this.container.clientHeight;
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
        this.renderer.setSize(width, height);
    }

    loadModel(url) {
        return new Promise((resolve, reject) => {
            // 기존 모델 제거
            if (this.currentModel) {
                this.scene.remove(this.currentModel);
                this.currentModel = null;
            }

            const loader = new THREE.GLTFLoader();
            loader.load(
                url,
                (gltf) => {
                    this.currentModel = gltf.scene;

                    // 모델 중앙 정렬 및 크기 조정
                    const box = new THREE.Box3().setFromObject(this.currentModel);
                    const center = box.getCenter(new THREE.Vector3());
                    const size = box.getSize(new THREE.Vector3());

                    const maxDim = Math.max(size.x, size.y, size.z);
                    const scale = 2 / maxDim;
                    this.currentModel.scale.multiplyScalar(scale);

                    this.currentModel.position.sub(center.multiplyScalar(scale));
                    this.currentModel.position.y = 0;

                    this.scene.add(this.currentModel);

                    // 카메라 위치 조정
                    this.camera.position.set(3, 2, 3);
                    this.controls.target.set(0, 0.5, 0);
                    this.controls.update();

                    resolve(gltf);
                },
                (progress) => {
                    console.log(`Loading: ${(progress.loaded / progress.total * 100).toFixed(1)}%`);
                },
                (error) => {
                    console.error('Model load error:', error);
                    reject(error);
                }
            );
        });
    }

    clear() {
        if (this.currentModel) {
            this.scene.remove(this.currentModel);
            this.currentModel = null;
        }
    }

    dispose() {
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
        }
        this.renderer.dispose();
        this.container.removeChild(this.renderer.domElement);
    }
}
