namespace TacoDiscordBot.Services;

public static class VcExchangeCalculator
{
    public const long ExchangeUnitMinutes = 6;
    public const long CoinsPerExchangeUnit = 25;

    public static long GetExchangeableMinutes(long unexchangedMinutes)
    {
        if (unexchangedMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(unexchangedMinutes));

        return unexchangedMinutes / ExchangeUnitMinutes * ExchangeUnitMinutes;
    }

    public static long GetCoins(long exchangeableMinutes)
    {
        if (exchangeableMinutes < 0 || exchangeableMinutes % ExchangeUnitMinutes != 0)
            throw new ArgumentOutOfRangeException(nameof(exchangeableMinutes));

        return exchangeableMinutes / ExchangeUnitMinutes * CoinsPerExchangeUnit;
    }
}
