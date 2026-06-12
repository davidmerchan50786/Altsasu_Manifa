// AlsasuaLogger.cs — Logger estático para todos los sistemas de Alsasua
using UnityEngine;

public static class AlsasuaLogger
{
    public static void Info(string sistema, string msg)
        => Debug.Log($"[{sistema}] {msg}");

    public static void Warn(string sistema, string msg)
        => Debug.LogWarning($"[{sistema}] {msg}");

    public static void Error(string sistema, string msg)
        => Debug.LogError($"[{sistema}] {msg}");
}
