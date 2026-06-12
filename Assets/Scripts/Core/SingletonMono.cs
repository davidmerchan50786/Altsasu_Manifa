// Assets/Scripts/SingletonMono.cs
// ═══════════════════════════════════════════════════════════════════════════
//  BASE CLASS — Singleton seguro para MonoBehaviour
//
//  Uso:
//    public class MiSistema : SingletonMono<MiSistema>
//    {
//        protected override void OnAwake() { /* lógica propia */ }
//    }
//
//  Por defecto destruye el COMPONENTE duplicado (Destroy(this)).
//  Para destruir el GO completo, sobreescribe DestroyGameObjectOnDuplicate:
//    protected override bool DestroyGameObjectOnDuplicate => true;
// ═══════════════════════════════════════════════════════════════════════════

using UnityEngine;

public abstract class SingletonMono<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }

    /// <summary>
    /// Si es true, un duplicado destruye el GameObject completo.
    /// Si es false (por defecto), solo destruye el componente.
    /// </summary>
    protected virtual bool DestroyGameObjectOnDuplicate => false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (DestroyGameObjectOnDuplicate) Destroy(gameObject);
            else                              Destroy(this);
            return;
        }
        Instance = this as T;
        OnAwake();
    }

    /// <summary>Sobreescribir en lugar de Awake() para lógica de inicialización.</summary>
    protected virtual void OnAwake() { }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        OnDestroyed();
    }

    /// <summary>Sobreescribir en lugar de OnDestroy() para limpieza.</summary>
    protected virtual void OnDestroyed() { }
}
