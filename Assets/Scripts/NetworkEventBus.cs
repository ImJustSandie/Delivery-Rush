using System;
using UnityEngine;

[Serializable]
public struct PlayerNetworkState
{
    public string entityId;
    public bool isMoving;
    public bool isCarrying;
    public int collectedCount;
    public int deliveredScore;
    public int powerUpCount;
    public int slowPowerUpCount;
    public int boostPowerUpCount;
    public int activeVisualEffect;
    public Vector3 position;
    public Quaternion rotation;
}

[Serializable]
public struct CubeNetworkState
{
    public string entityId;
    public bool isBeingCarried;
    public bool isGrounded;
    public Vector3 position;
}

/// <summary>
/// Punto unico de publicacion de estados. El futuro transporte
/// (WebSockets) solo tiene que suscribirse aqui para enviar al host.
/// </summary>
public static class NetworkEventBus
{
    public static event Action<PlayerNetworkState> PlayerStateChanged;
    public static event Action<CubeNetworkState> CubeStateChanged;

    public static void Publish(PlayerNetworkState state)
    {
        PlayerStateChanged?.Invoke(state);
    }

    public static void Publish(CubeNetworkState state)
    {
        CubeStateChanged?.Invoke(state);
    }
}
