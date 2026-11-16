namespace Tools.Georeferencing
{
    // Keep serialized values stable when adding providers.
    public enum LocationOrigin
    {
        Unknown = 0, Cellular = 1, Satellite = 2, WiFi = 3, IPAddress = 4,
        Default = 5, Obfuscated = 6, DeviceLocation = 7, ConfigurationOverride = 8, Simulated = 9
    }

    public enum LocationPrecision { Unknown = 0, Approximate = 1, Precise = 2 }
}
