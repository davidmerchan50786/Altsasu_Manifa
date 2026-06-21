// Assets/Scripts/_Impostores~/ImpostorAtlasSO.cs   (STAGING — Unity no compila carpetas con ~)
// ─────────────────────────────────────────────────────────────────────────────
//  Datos del atlas de impostores (billboard). Fase 1: albedo + N vistas yaw.
//  Para activar: mover esta carpeta a Assets/Scripts/Runtime/Impostores (Runtime)
//  y el baker a Assets/Scripts/Editor. Ver LEEME_impostores.md.
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Alsasua/Impostor Atlas", fileName = "ImpostorAtlasSO")]
public class ImpostorAtlasSO : ScriptableObject
{
    [Tooltip("Atlas de albedo (RGBA). Cada edificio ocupa una tira horizontal de 'vistasYaw' celdas.")]
    public Texture2D albedoAtlas;

    [Tooltip("Número de vistas horizontales (columnas) por edificio.")]
    public int vistasYaw = 8;

    [Tooltip("Resolución en px de cada vista (celda cuadrada).")]
    public int celdaPx = 256;

    [Tooltip("Lado del atlas en px.")]
    public int atlasPx = 4096;

    [Serializable]
    public struct Entrada
    {
        public long id;            // id OSM del edificio (mismo que FacadeTextures)
        public Rect uvTira;        // rect normalizado de la TIRA completa (vistasYaw celdas) en el atlas
        public float anchoMundo;   // tamaño del quad en metros (X)
        public float altoMundo;    // tamaño del quad en metros (Y)
        public Vector3 pivotMundo; // base (centro inferior) del edificio en coords mundo Unity
    }

    public List<Entrada> entradas = new List<Entrada>();

    /// <summary>UV de una vista concreta (0..vistasYaw-1) dentro de la tira de una entrada.</summary>
    public Rect UvDeVista(in Entrada e, int vista)
    {
        float w = e.uvTira.width / vistasYaw;
        return new Rect(e.uvTira.x + w * Mathf.Clamp(vista, 0, vistasYaw - 1), e.uvTira.y, w, e.uvTira.height);
    }

    public bool TryGet(long id, out Entrada entrada)
    {
        for (int i = 0; i < entradas.Count; i++)
            if (entradas[i].id == id) { entrada = entradas[i]; return true; }
        entrada = default;
        return false;
    }
}
