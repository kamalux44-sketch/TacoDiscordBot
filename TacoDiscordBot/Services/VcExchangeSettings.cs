namespace TacoDiscordBot.Services;

public static class VcExchangeSettings
{
    public const int ExchangeUnitMinutes = 6;
    public const long ExchangeCoinsPerUnit = 25;
    public const long ExchangeUnitSeconds = ExchangeUnitMinutes * 60L;
}
