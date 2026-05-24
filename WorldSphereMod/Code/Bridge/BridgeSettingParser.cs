using System;
using System.Globalization;

namespace WorldSphereMod.Bridge
{
    /// <summary>
    /// Query-string value parsing for BridgeRPC <c>/settings/{key}?value=</c> updates.
    /// Kept Unity-free so unit tests can fuzz malformed inputs without launching the game.
    /// </summary>
    public static class BridgeSettingParser
    {
        public static bool TryParseSettingValue(Type fieldType, string rawValue, out object? parsed, out string error)
        {
            parsed = null;
            error = string.Empty;
            try
            {
                if (fieldType == typeof(string)) { parsed = rawValue; return true; }
                if (fieldType == typeof(bool))
                {
                    if (bool.TryParse(rawValue, out bool boolValue) || string.Equals(rawValue, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase))
                    {
                        parsed = !string.Equals(rawValue, "0", StringComparison.OrdinalIgnoreCase) && (bool.TryParse(rawValue, out boolValue) ? boolValue : true);
                        return true;
                    }
                    error = "invalid_bool";
                    return false;
                }
                if (fieldType == typeof(int)) { if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue)) { parsed = intValue; return true; } error = "invalid_int"; return false; }
                if (fieldType == typeof(float)) { if (float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float floatValue)) { parsed = floatValue; return true; } error = "invalid_float"; return false; }
                if (fieldType.IsEnum) { parsed = Enum.Parse(fieldType, rawValue, ignoreCase: true); return true; }
                parsed = Convert.ChangeType(rawValue, fieldType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex)
            {
                error = "invalid_value:" + ex.Message;
                return false;
            }
        }

        public static bool TryParseNonNegativeInt(string text, out int value)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;
        }
    }
}
