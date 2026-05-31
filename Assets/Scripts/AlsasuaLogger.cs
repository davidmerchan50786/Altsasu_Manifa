// Assets/Scripts/AlsasuaLogger.cs
// Thin logging wrapper — keeps simulator scripts compiling without modification.
// All calls use (category, message) signature.

using UnityEngine;

public static class AlsasuaLogger
{
    public static void Info(string categoria, string msg)
        => Debug.Log($"[{categoria}] {msg}");

    public static void Warn(string categoria, string msg)
        => Debug.LogWarning($"[{categoria}] {msg}");

    public static void Error(string categoria, string msg)
        => Debug.LogError($"[{categoria}] {msg}");

    public static void Verbose(string categoria, string msg)
        => Debug.Log($"[{categoria}:V] {msg}");

    // Single-arg overloads for any calls without category
    public static void Info(string msg)    => Debug.Log($"[Alsasua] {msg}");
    public static void Warn(string msg)    => Debug.LogWarning($"[Alsasua] {msg}");
    public static void Error(string msg)   => Debug.LogError($"[Alsasua] {msg}");
    public static void Verbose(string msg) => Debug.Log($"[Alsasua:V] {msg}");
}
