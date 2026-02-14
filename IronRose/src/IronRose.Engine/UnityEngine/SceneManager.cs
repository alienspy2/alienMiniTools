using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public static class SceneManager
    {
        // --- Core registries ---
        private static readonly List<MonoBehaviour> _behaviours = new();
        private static readonly List<MonoBehaviour> _pendingStart = new();
        private static readonly List<GameObject> _allGameObjects = new();

        // --- Coroutines ---
        private static readonly List<Coroutine> _coroutines = new();

        // --- Invoke ---
        private static readonly List<InvokeEntry> _invokeEntries = new();

        // --- Deferred destroy ---
        private static readonly List<DestroyEntry> _destroyQueue = new();

        public static IReadOnlyList<GameObject> AllGameObjects => _allGameObjects;

        // ================================================================
        // Registration
        // ================================================================

        public static void RegisterGameObject(GameObject go)
        {
            _allGameObjects.Add(go);
        }

        public static void RegisterBehaviour(MonoBehaviour behaviour)
        {
            // Duplicate guard
            if (_behaviours.Contains(behaviour)) return;

            _behaviours.Add(behaviour);

            try
            {
                behaviour.Awake();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception in Awake() of {behaviour.GetType().Name}: {ex.Message}");
            }

            behaviour._hasAwoken = true;

            // OnEnable after Awake
            if (behaviour.enabled && behaviour.gameObject.activeSelf)
            {
                try { behaviour.OnEnable(); }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in OnEnable() of {behaviour.GetType().Name}: {ex.Message}");
                }
            }

            _pendingStart.Add(behaviour);
        }

        // ================================================================
        // Fixed Update Loop (physics)
        // ================================================================

        public static void FixedUpdate(float fixedDeltaTime)
        {
            for (int i = 0; i < _behaviours.Count; i++)
            {
                var b = _behaviours[i];
                if (!IsActive(b)) continue;
                try { b.FixedUpdate(); }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in FixedUpdate() of {b.GetType().Name}: {ex.Message}");
                }
            }
        }

        // ================================================================
        // Main Update Loop
        // ================================================================

        public static void Update(float deltaTime)
        {
            Time.unscaledDeltaTime = deltaTime;
            Time.deltaTime = deltaTime * Time.timeScale;
            Time.time += Time.deltaTime;

            // 1. Process pending Start() calls
            if (_pendingStart.Count > 0)
            {
                // Copy to avoid mutation during iteration
                var pending = new List<MonoBehaviour>(_pendingStart);
                _pendingStart.Clear();

                foreach (var b in pending)
                {
                    if (!IsActive(b)) continue;
                    try { b.Start(); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in Start() of {b.GetType().Name}: {ex.Message}");
                    }
                }
            }

            // 2. Process Invokes
            ProcessInvokes(Time.deltaTime);

            // 3. Update all behaviours
            for (int i = 0; i < _behaviours.Count; i++)
            {
                var b = _behaviours[i];
                if (!IsActive(b)) continue;
                try { b.Update(); }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in Update() of {b.GetType().Name}: {ex.Message}");
                }
            }

            // 4. Process coroutines (after Update, before LateUpdate — matches Unity)
            ProcessCoroutines(Time.deltaTime);

            // 5. LateUpdate all behaviours
            for (int i = 0; i < _behaviours.Count; i++)
            {
                var b = _behaviours[i];
                if (!IsActive(b)) continue;
                try { b.LateUpdate(); }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in LateUpdate() of {b.GetType().Name}: {ex.Message}");
                }
            }

            // 6. Process deferred destroy queue
            ProcessDestroyQueue(Time.deltaTime);

            Time.frameCount++;
        }

        private static bool IsActive(MonoBehaviour b)
        {
            return b.enabled && !b._isDestroyed && b.gameObject.activeInHierarchy;
        }

        // ================================================================
        // Coroutine Processing
        // ================================================================

        internal static void AddCoroutine(Coroutine coroutine)
        {
            // Advance once immediately (run to first yield)
            if (AdvanceCoroutine(coroutine))
                _coroutines.Add(coroutine);
        }

        internal static void StopCoroutine(MonoBehaviour owner, string methodName)
        {
            foreach (var c in _coroutines)
            {
                if (c.owner == owner && c.routine.GetType().Name.Contains(methodName))
                    c.isDone = true;
            }
        }

        internal static void StopAllCoroutines(MonoBehaviour owner)
        {
            foreach (var c in _coroutines)
            {
                if (c.owner == owner)
                    c.isDone = true;
            }
        }

        private static void ProcessCoroutines(float deltaTime)
        {
            for (int i = _coroutines.Count - 1; i >= 0; i--)
            {
                var c = _coroutines[i];

                if (c.isDone || c.owner._isDestroyed || !c.owner.enabled)
                {
                    _coroutines.RemoveAt(i);
                    continue;
                }

                // Waiting on timer
                if (c.waitTimer > 0f)
                {
                    c.waitTimer -= deltaTime;
                    if (c.waitTimer > 0f) continue;
                }

                // Advance
                if (!AdvanceCoroutine(c))
                {
                    _coroutines.RemoveAt(i);
                }
            }
        }

        private static bool AdvanceCoroutine(Coroutine c)
        {
            bool hasMore;
            try { hasMore = c.routine.MoveNext(); }
            catch (Exception ex)
            {
                Debug.LogError($"Exception in coroutine of {c.owner.GetType().Name}: {ex.Message}");
                c.isDone = true;
                return false;
            }

            if (!hasMore)
            {
                c.isDone = true;
                return false;
            }

            var current = c.routine.Current;
            switch (current)
            {
                case null:
                    // yield return null — resume next frame
                    c.waitTimer = 0f;
                    break;
                case WaitForSeconds wfs:
                    c.waitTimer = wfs.duration;
                    break;
                case WaitForEndOfFrame:
                    c.waitTimer = 0f;
                    break;
                case WaitForFixedUpdate:
                    c.waitTimer = 0f;
                    break;
                case Coroutine nested:
                    // Wait for nested coroutine — poll each frame
                    c.waitTimer = 0f;
                    // Replace routine with a wrapper that waits for nested
                    c.routine = WaitForNestedCoroutine(nested, c.routine);
                    break;
                case CustomYieldInstruction custom:
                    c.routine = WaitForCustomYield(custom, c.routine);
                    break;
                case IEnumerator nestedRoutine:
                    // Auto-wrap as nested coroutine
                    var nestedCoroutine = new Coroutine(nestedRoutine, c.owner);
                    _coroutines.Add(nestedCoroutine);
                    c.routine = WaitForNestedCoroutine(nestedCoroutine, c.routine);
                    break;
            }

            return true;
        }

        private static IEnumerator WaitForNestedCoroutine(Coroutine nested, IEnumerator continuation)
        {
            while (!nested.isDone)
                yield return null;
            // Continue with original routine
            while (continuation.MoveNext())
                yield return continuation.Current;
        }

        private static IEnumerator WaitForCustomYield(CustomYieldInstruction custom, IEnumerator continuation)
        {
            while (custom.keepWaiting)
                yield return null;
            while (continuation.MoveNext())
                yield return continuation.Current;
        }

        // ================================================================
        // Invoke Processing
        // ================================================================

        internal static void ScheduleInvoke(MonoBehaviour target, string methodName, float delay, float repeatRate, bool repeating)
        {
            _invokeEntries.Add(new InvokeEntry
            {
                target = target,
                methodName = methodName,
                timer = delay,
                repeatRate = repeatRate,
                repeating = repeating,
            });
        }

        internal static void CancelAllInvokes(MonoBehaviour target)
        {
            _invokeEntries.RemoveAll(e => e.target == target);
        }

        internal static void CancelInvoke(MonoBehaviour target, string methodName)
        {
            _invokeEntries.RemoveAll(e => e.target == target && e.methodName == methodName);
        }

        internal static bool IsInvoking(MonoBehaviour target)
        {
            foreach (var e in _invokeEntries)
                if (e.target == target) return true;
            return false;
        }

        internal static bool IsInvoking(MonoBehaviour target, string methodName)
        {
            foreach (var e in _invokeEntries)
                if (e.target == target && e.methodName == methodName) return true;
            return false;
        }

        private static void ProcessInvokes(float deltaTime)
        {
            for (int i = _invokeEntries.Count - 1; i >= 0; i--)
            {
                var entry = _invokeEntries[i];
                if (entry.target._isDestroyed)
                {
                    _invokeEntries.RemoveAt(i);
                    continue;
                }

                entry.timer -= deltaTime;
                if (entry.timer <= 0f)
                {
                    // Fire
                    try
                    {
                        var method = entry.target.GetType().GetMethod(entry.methodName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        method?.Invoke(entry.target, null);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in Invoke '{entry.methodName}' of {entry.target.GetType().Name}: {ex.Message}");
                    }

                    if (entry.repeating)
                    {
                        entry.timer = entry.repeatRate;
                        _invokeEntries[i] = entry;
                    }
                    else
                    {
                        _invokeEntries.RemoveAt(i);
                    }
                }
                else
                {
                    _invokeEntries[i] = entry;
                }
            }
        }

        // ================================================================
        // Destroy
        // ================================================================

        internal static void ScheduleDestroy(Object obj, float delay)
        {
            _destroyQueue.Add(new DestroyEntry { target = obj, timer = delay });
        }

        internal static void DestroyImmediate(Object obj)
        {
            ExecuteDestroy(obj);
        }

        private static void ProcessDestroyQueue(float deltaTime)
        {
            for (int i = _destroyQueue.Count - 1; i >= 0; i--)
            {
                var entry = _destroyQueue[i];
                entry.timer -= deltaTime;

                if (entry.timer <= 0f)
                {
                    ExecuteDestroy(entry.target);
                    _destroyQueue.RemoveAt(i);
                }
                else
                {
                    _destroyQueue[i] = entry;
                }
            }
        }

        private static void ExecuteDestroy(Object obj)
        {
            if (obj._isDestroyed) return;

            if (obj is GameObject go)
            {
                // Destroy children first
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                    ExecuteDestroy(go.transform.GetChild(i).gameObject);

                // OnDisable + OnDestroy for all MonoBehaviours
                foreach (var comp in go._components)
                {
                    if (comp is MonoBehaviour mb && !mb._isDestroyed)
                    {
                        try
                        {
                            if (mb._hasAwoken && mb.enabled) mb.OnDisable();
                            mb.OnDestroy();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Exception in OnDestroy() of {mb.GetType().Name}: {ex.Message}");
                        }
                        StopAllCoroutines(mb);
                        CancelAllInvokes(mb);
                        _behaviours.Remove(mb);
                        _pendingStart.Remove(mb);
                    }

                    if (comp is MeshRenderer mr)
                        MeshRenderer._allRenderers.Remove(mr);

                    if (comp is SpriteRenderer spr)
                        SpriteRenderer._allSpriteRenderers.Remove(spr);

                    if (comp is TextRenderer txr)
                        TextRenderer._allTextRenderers.Remove(txr);

                    if (comp is Light light)
                        Light._allLights.Remove(light);

                    if (comp is Rigidbody rb3)
                    {
                        rb3.RemoveFromPhysics();
                        Rigidbody._allRigidbodies.Remove(rb3);
                    }

                    if (comp is Rigidbody2D rb2d3)
                    {
                        rb2d3.RemoveFromPhysics();
                        Rigidbody2D._allRigidbodies2D.Remove(rb2d3);
                    }

                    if (comp is Camera cam && Camera.main == cam)
                        Camera.ClearMain();

                    comp._isDestroyed = true;
                }

                // Remove from parent
                go.transform.SetParent(null, false);
                _allGameObjects.Remove(go);
                go._isDestroyed = true;
            }
            else if (obj is Component comp)
            {
                if (comp is MonoBehaviour mb)
                {
                    try
                    {
                        if (mb._hasAwoken && mb.enabled) mb.OnDisable();
                        mb.OnDestroy();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in OnDestroy() of {mb.GetType().Name}: {ex.Message}");
                    }
                    StopAllCoroutines(mb);
                    CancelAllInvokes(mb);
                    _behaviours.Remove(mb);
                    _pendingStart.Remove(mb);
                }

                if (comp is MeshRenderer mr)
                    MeshRenderer._allRenderers.Remove(mr);

                if (comp is SpriteRenderer spr)
                    SpriteRenderer._allSpriteRenderers.Remove(spr);

                if (comp is TextRenderer txr)
                    TextRenderer._allTextRenderers.Remove(txr);

                if (comp is Light light)
                    Light._allLights.Remove(light);

                if (comp is Rigidbody rb)
                {
                    rb.RemoveFromPhysics();
                    Rigidbody._allRigidbodies.Remove(rb);
                }

                if (comp is Rigidbody2D rb2d)
                {
                    rb2d.RemoveFromPhysics();
                    Rigidbody2D._allRigidbodies2D.Remove(rb2d);
                }

                comp.gameObject.RemoveComponent(comp);
                comp._isDestroyed = true;
            }
        }

        // ================================================================
        // Clear (hot-reload / scene change)
        // ================================================================

        public static void Clear()
        {
            foreach (var b in _behaviours)
            {
                try
                {
                    if (b._hasAwoken && b.enabled) b.OnDisable();
                    b.OnDestroy();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in OnDestroy() of {b.GetType().Name}: {ex.Message}");
                }
            }

            _behaviours.Clear();
            _pendingStart.Clear();
            _allGameObjects.Clear();
            _coroutines.Clear();
            _invokeEntries.Clear();
            _destroyQueue.Clear();

            // Clear rendering registries
            MeshRenderer.ClearAll();
            SpriteRenderer.ClearAll();
            TextRenderer.ClearAll();
            Light.ClearAll();
            Camera.ClearMain();

            // Clear physics registries
            Rigidbody.ClearAll();
            Rigidbody2D.ClearAll();

            // Reset physics worlds (BepuPhysics / Aether body 제거)
            IronRose.Engine.PhysicsManager.Instance?.Reset();
        }

        // ================================================================
        // Internal types
        // ================================================================

        private struct InvokeEntry
        {
            public MonoBehaviour target;
            public string methodName;
            public float timer;
            public float repeatRate;
            public bool repeating;
        }

        private struct DestroyEntry
        {
            public Object target;
            public float timer;
        }
    }
}
