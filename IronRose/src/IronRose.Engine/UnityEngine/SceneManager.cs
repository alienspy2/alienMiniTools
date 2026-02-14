using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public static class SceneManager
    {
        private static readonly List<MonoBehaviour> _behaviours = new();
        private static readonly List<MonoBehaviour> _pendingStart = new();

        public static void RegisterBehaviour(MonoBehaviour behaviour)
        {
            _behaviours.Add(behaviour);

            try
            {
                behaviour.Awake();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception in Awake() of {behaviour.GetType().Name}: {ex.Message}");
            }

            _pendingStart.Add(behaviour);
        }

        public static void Update(float deltaTime)
        {
            Time.deltaTime = deltaTime;
            Time.time += deltaTime;

            // Process pending Start() calls
            if (_pendingStart.Count > 0)
            {
                foreach (var b in _pendingStart)
                {
                    if (!b.enabled) continue;
                    try
                    {
                        b.Start();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in Start() of {b.GetType().Name}: {ex.Message}");
                    }
                }
                _pendingStart.Clear();
            }

            // Update all behaviours
            foreach (var b in _behaviours)
            {
                if (!b.enabled) continue;
                try
                {
                    b.Update();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in Update() of {b.GetType().Name}: {ex.Message}");
                }
            }

            // LateUpdate all behaviours
            foreach (var b in _behaviours)
            {
                if (!b.enabled) continue;
                try
                {
                    b.LateUpdate();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in LateUpdate() of {b.GetType().Name}: {ex.Message}");
                }
            }

            Time.frameCount++;
        }

        public static void Clear()
        {
            foreach (var b in _behaviours)
            {
                try
                {
                    b.OnDestroy();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception in OnDestroy() of {b.GetType().Name}: {ex.Message}");
                }
            }

            _behaviours.Clear();
            _pendingStart.Clear();
        }
    }
}
