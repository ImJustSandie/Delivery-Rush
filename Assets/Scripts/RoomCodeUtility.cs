using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Utilidad para convertir direcciones IP locales (IPv4) a códigos cortos de 4 caracteres (Base36)
/// y viceversa. Permite a los jugadores conectarse usando un código de sala sencillo en lugar de ingresar la IP.
/// </summary>
public static class RoomCodeUtility
{
    private const string Base36Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static readonly string[] SupportedPrefixes = new string[]
    {
        "192.168.", // Índice 0 (Más común en redes domésticas)
        "10.0.",    // Índice 1 (Redes privadas de clase A)
        "172.16.",  // Índice 2 (Redes privadas de clase B)
        "127.0."    // Índice 3 (Loopback local)
    };

    /// <summary>
    /// Convierte una dirección IPv4 (ej. 192.168.1.15) en un código de sala de 4 caracteres.
    /// </summary>
    /// <param name="ipAddress">Dirección IPv4 a codificar.</param>
    /// <returns>Código alfanumérico de 4 caracteres.</returns>
    public static string IPToCode(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return "0000";

        ipAddress = ipAddress.Trim();

        // Si es 0.0.0.0 o localhost, intentar obtener la IP local real de la máquina
        if (ipAddress == "0.0.0.0" || ipAddress == "127.0.0.1")
        {
            string localIP = LobbyUIHandler.GetLocalIPAddress();
            if (!string.IsNullOrEmpty(localIP) && localIP != "127.0.0.1")
            {
                ipAddress = localIP;
            }
        }

        string[] parts = ipAddress.Split('.');
        if (parts.Length != 4)
            return "0000";

        if (!byte.TryParse(parts[0], out byte a) ||
            !byte.TryParse(parts[1], out byte b) ||
            !byte.TryParse(parts[2], out byte c) ||
            !byte.TryParse(parts[3], out byte d))
        {
            return "0000";
        }

        string prefix = $"{a}.{b}.";
        int prefixIndex = Array.IndexOf(SupportedPrefixes, prefix);
        if (prefixIndex < 0)
        {
            prefixIndex = 0; // Fallback predeterminado a 192.168.
        }

        long numericValue = ((long)prefixIndex * 65536) + ((long)c * 256) + d;
        return EncodeBase36(numericValue, 4);
    }

    /// <summary>
    /// Convierte un código de sala de 4 caracteres (o una IP completa ingresada directamente) a una IP IPv4 válida.
    /// </summary>
    /// <param name="input">Código de 4 caracteres o dirección IP.</param>
    /// <param name="defaultPrefix">Prefijo predeterminado si falla la detección.</param>
    /// <returns>Dirección IPv4 resuelta.</returns>
    public static string CodeToIP(string input, string defaultPrefix = "192.168.")
    {
        if (string.IsNullOrWhiteSpace(input))
            return "127.0.0.1";

        input = input.Trim().ToUpper();

        // Si el usuario ingresó directamente una dirección IP válida (ej. 192.168.1.15)
        if (input.Contains("."))
            return input;

        long numericValue = DecodeBase36(input);
        if (numericValue < 0)
            return "127.0.0.1";

        int prefixIndex = (int)(numericValue / 65536);
        long remainder = numericValue % 65536;

        int c = (int)(remainder / 256);
        int d = (int)(remainder % 256);

        string prefix = defaultPrefix;
        if (prefixIndex >= 0 && prefixIndex < SupportedPrefixes.Length)
        {
            prefix = SupportedPrefixes[prefixIndex];
        }

        return $"{prefix}{c}.{d}";
    }

    private static string EncodeBase36(long value, int minLength)
    {
        if (value == 0) return new string('0', minLength);

        StringBuilder sb = new StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, Base36Chars[(int)(value % 36)]);
            value /= 36;
        }

        while (sb.Length < minLength)
        {
            sb.Insert(0, '0');
        }

        return sb.ToString();
    }

    private static long DecodeBase36(string input)
    {
        long value = 0;
        foreach (char ch in input)
        {
            int index = Base36Chars.IndexOf(ch);
            if (index < 0) return -1;
            value = value * 36 + index;
        }
        return value;
    }
}
