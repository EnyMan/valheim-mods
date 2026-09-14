using BepInEx.Configuration;
using Jotunn.Utils;

namespace WeatherAltar
{
    public static class AltarSettings
    {
        private const string Section = "Offering";

        public static ConfigEntry<int> CoinCost;
        public static ConfigEntry<float> HealthCost;
        public static ConfigEntry<int> DurationSeconds;

        public static void Init(ConfigFile config)
        {
            var adminOnly = new ConfigurationManagerAttributes { IsAdminOnly = true };

            CoinCost = config.Bind(Section, nameof(CoinCost), 10,
                new ConfigDescription("Coins required per offering.",
                    new AcceptableValueRange<int>(1, 10000), adminOnly));

            HealthCost = config.Bind(Section, nameof(HealthCost), 10f,
                new ConfigDescription("Health sacrificed per offering (nonlethal).",
                    new AcceptableValueRange<float>(1f, 1000f), adminOnly));

            DurationSeconds = config.Bind(Section, nameof(DurationSeconds), 600,
                new ConfigDescription("How long the purchased weather lasts, in real seconds.",
                    new AcceptableValueRange<int>(10, 3600), adminOnly));
        }
    }
}