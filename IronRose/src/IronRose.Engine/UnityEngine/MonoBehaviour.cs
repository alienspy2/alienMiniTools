using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UnityEngine
{
    public class MonoBehaviour : Component
    {
        internal bool _hasAwoken;

        private bool _enabled = true;
        public bool enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (_hasAwoken && gameObject != null && gameObject.activeSelf)
                {
                    try
                    {
                        if (_enabled) OnEnable();
                        else OnDisable();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Exception in {(_enabled ? "OnEnable" : "OnDisable")}() of {GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        // --- Lifecycle methods ---
        public virtual void Awake() { }
        public virtual void OnEnable() { }
        public virtual void Start() { }
        public virtual void Update() { }
        public virtual void LateUpdate() { }
        public virtual void OnDisable() { }
        public virtual void OnDestroy() { }

        // --- Coroutines ---
        public Coroutine StartCoroutine(IEnumerator routine)
        {
            var coroutine = new Coroutine(routine, this);
            SceneManager.AddCoroutine(coroutine);
            return coroutine;
        }

        public Coroutine StartCoroutine(string methodName)
        {
            var method = GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
                throw new ArgumentException($"Coroutine '{methodName}' not found on {GetType().Name}");

            var routine = (IEnumerator)method.Invoke(this, null)!;
            return StartCoroutine(routine);
        }

        public void StopCoroutine(Coroutine coroutine)
        {
            if (coroutine != null)
                coroutine.isDone = true;
        }

        public void StopCoroutine(string methodName)
        {
            SceneManager.StopCoroutine(this, methodName);
        }

        public void StopAllCoroutines()
        {
            SceneManager.StopAllCoroutines(this);
        }

        // --- Invoke ---
        public void Invoke(string methodName, float time)
        {
            SceneManager.ScheduleInvoke(this, methodName, time, 0f, false);
        }

        public void InvokeRepeating(string methodName, float time, float repeatRate)
        {
            SceneManager.ScheduleInvoke(this, methodName, time, repeatRate, true);
        }

        public void CancelInvoke()
        {
            SceneManager.CancelAllInvokes(this);
        }

        public void CancelInvoke(string methodName)
        {
            SceneManager.CancelInvoke(this, methodName);
        }

        public bool IsInvoking() => SceneManager.IsInvoking(this);

        public bool IsInvoking(string methodName) => SceneManager.IsInvoking(this, methodName);
    }
}
